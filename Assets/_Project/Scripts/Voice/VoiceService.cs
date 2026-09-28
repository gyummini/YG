using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Vivox in-game voice. Everyone joins one non-positional channel with an open mic; every remote
    /// participant gets a participant tap (silenced in Vivox's own mix) whose AudioSource is handed to that
    /// player's <see cref="RemoteVoice"/>, where the voice rules (proximity / cut / muffle / radio) are applied.
    /// </summary>
    public class VoiceService : SceneSingleton<VoiceService>
    {
        public enum VoiceState
        {
            Off,
            Starting,
            LoggedIn,
            Joining,
            InChannel,
            Failed,
        }

        public VoiceState State { get; private set; } = VoiceState.Off;
        public string LastError { get; private set; } = "";
        public string Channel { get; private set; } = "";
        public int RemoteParticipants => m_Taps.Count;

        readonly Dictionary<string, GameObject> m_Taps = new Dictionary<string, GameObject>();
        readonly Dictionary<string, VivoxParticipant> m_Participants = new Dictionary<string, VivoxParticipant>();
        bool m_Subscribed;
        string m_WantChannel;

        public static bool ForceCaptureSilence =>
            InstanceInfo.HasTag("NoMic") || Environment.GetCommandLineArgs().Contains("-nomic") ||
            (AutoTestRequest.Current != null && AutoTestRequest.Current.muteMic);

        public static string TestVoiceWavPath => System.IO.Path.Combine(Application.dataPath, "_Project/Audio/Generated/test_voice.wav");

        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case VoiceState.InChannel: return $"음성 연결됨 (상대 {RemoteParticipants})";
                    case VoiceState.Failed: return "음성 사용 불가: " + LastError;
                    case VoiceState.Off: return "음성 꺼짐";
                    default: return "음성 연결 중…";
                }
            }
        }

        /// <summary>Join (or switch to) the session's voice channel.</summary>
        public async void Join(string channel)
        {
            if (string.IsNullOrEmpty(channel) || channel == Channel && State == VoiceState.InChannel) return;
            m_WantChannel = channel;
            try
            {
                State = VoiceState.Starting;
                if (!await UgsBootstrap.EnsureAsync())
                {
                    Fail(UgsBootstrap.FriendlyError);
                    return;
                }
                if (!VivoxService.Instance.IsLoggedIn)
                {
                    var cfg = new VivoxConfigurationOptions { DisableAudioDucking = true, ForceCaptureSilence = ForceCaptureSilence };
                    await VivoxService.Instance.InitializeAsync(cfg);
                    Subscribe();
                    await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = UgsBootstrap.DisplayName, EnableTTS = true });
                }
                Subscribe();
                State = VoiceState.LoggedIn;
                if (!string.IsNullOrEmpty(Channel) && Channel != channel)
                {
                    try { await VivoxService.Instance.LeaveChannelAsync(Channel); } catch (Exception) { }
                }
                if (m_WantChannel != channel) return;
                State = VoiceState.Joining;
                Channel = channel;
                await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                VivoxService.Instance.UnmuteInputDevice();
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        void Fail(string message)
        {
            State = VoiceState.Failed;
            LastError = string.IsNullOrEmpty(message) ? "Vivox 초기화 실패 (Unity Cloud 연결과 Vivox 활성화를 확인하세요)" : message;
            GameLog.Warn("Voice", LastError);
        }

        public async void Leave()
        {
            var ch = Channel;
            Channel = "";
            m_WantChannel = null;
            ClearTaps();
            if (State == VoiceState.InChannel || State == VoiceState.Joining)
            {
                try { await VivoxService.Instance.LeaveChannelAsync(ch); } catch (Exception) { }
            }
            if (State != VoiceState.Failed) State = VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn ? VoiceState.LoggedIn : VoiceState.Off;
        }

        void Subscribe()
        {
            if (m_Subscribed || VivoxService.Instance == null) return;
            m_Subscribed = true;
            VivoxService.Instance.ChannelJoined += OnChannelJoined;
            VivoxService.Instance.ChannelLeft += OnChannelLeft;
            VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
        }

        protected override void OnDestroy()
        {
            if (m_Subscribed && VivoxService.Instance != null)
            {
                VivoxService.Instance.ChannelJoined -= OnChannelJoined;
                VivoxService.Instance.ChannelLeft -= OnChannelLeft;
                VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            }
            base.OnDestroy();
        }

        void OnChannelJoined(string channel)
        {
            if (channel != Channel) return;
            State = VoiceState.InChannel;
            GameLog.Info("Voice", $"채널 참가 {channel}");
        }

        void OnChannelLeft(string channel)
        {
            if (channel == Channel && State == VoiceState.InChannel) State = VoiceState.LoggedIn;
        }

        void OnParticipantAdded(VivoxParticipant p)
        {
            if (p.IsSelf || p.ChannelName != Channel) return;
            m_Participants[p.PlayerId] = p;
            var go = p.CreateVivoxParticipantTap("VoiceTap_" + p.PlayerId, true);
            if (go != null)
            {
                m_Taps[p.PlayerId] = go;
                GameLog.Info("Voice", $"참가자 탭 생성 {p.DisplayName} ({p.PlayerId})");
            }
        }

        void OnParticipantRemoved(VivoxParticipant p)
        {
            if (m_Taps.TryGetValue(p.PlayerId, out var go))
            {
                foreach (var pl in PlayerNet.All)
                    if (pl != null && pl.remoteVoice != null)
                        pl.remoteVoice.DetachTap(go);
                m_Taps.Remove(p.PlayerId);
            }
            m_Participants.Remove(p.PlayerId);
        }

        void ClearTaps()
        {
            foreach (var kv in m_Participants)
            {
                try { kv.Value.DestroyVivoxParticipantTap(); } catch (Exception) { }
            }
            m_Taps.Clear();
            m_Participants.Clear();
        }

        void Update()
        {
            // Bind taps to the matching remote players (by Unity Authentication player id).
            foreach (var kv in m_Taps)
            {
                if (kv.Value == null) continue;
                foreach (var pl in PlayerNet.All)
                {
                    if (pl == null || pl.IsOwner || pl.remoteVoice == null) continue;
                    if (pl.AuthId.Value.ToString() == kv.Key) pl.remoteVoice.AttachTap(kv.Value);
                }
            }
        }

        public bool TryGetParticipant(string playerId, out VivoxParticipant p) => m_Participants.TryGetValue(playerId, out p);

        /// <summary>Stream a wav file into our own transmission (automated tests without a microphone).</summary>
        public void StartInjection(string wavPath)
        {
            if (State != VoiceState.InChannel) return;
            try { VivoxService.Instance.StartAudioInjection(wavPath); }
            catch (Exception e) { GameLog.Warn("Voice", "injection failed: " + e.Message); }
        }

        public void StopInjection()
        {
            try { if (VivoxService.Instance != null && VivoxService.Instance.IsInjectingAudio) VivoxService.Instance.StopAudioInjection(); }
            catch (Exception) { }
        }
    }

    static class StringArrayExt
    {
        public static bool Contains(this string[] arr, string value)
        {
            foreach (var s in arr)
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
