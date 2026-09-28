using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>What one instance currently perceives (for automated assertions).</summary>
    [Serializable]
    public struct Observation
    {
        public string instance;
        public string zone;
        public string otherRoute;
        public float otherTestLevel;
        public float otherTapLevel;
        public float otherPeakLevel;
        public float duckDb;
        public float voiceLevel;
        public bool radioGranted;
        public bool radioReceiving;
        public bool radioDeadPress;
        public int squelchCount;
        public int busyCount;
        public int relayCount;
        public string lastTxResult;
        public string role;
        public string voiceState;
        public bool officeDoorAcousticOpen;
        public bool usingRelay;
        public string joinCode;
        public bool otherHasTap;
        public int vivoxParticipants;

        public static Observation Capture()
        {
            var o = new Observation { instance = InstanceInfo.IsMainEditor ? "host" : "client" };
            var local = PlayerNet.Local;
            if (local != null)
            {
                o.zone = local.Zone != null ? local.Zone.label : "-";
                o.role = local.Role.ToString();
                var other = local.Other;
                if (other != null && other.remoteVoice != null)
                {
                    o.otherRoute = other.remoteVoice.Route.Mode.ToString();
                    o.otherTestLevel = other.remoteVoice.TestLevel;
                    o.otherTapLevel = other.remoteVoice.TapLevel;
                    o.otherPeakLevel = other.remoteVoice.PeakLevel;
                }
            }
            if (Ducker.I != null)
            {
                o.duckDb = Ducker.I.GainDb;
                o.voiceLevel = Ducker.I.VoiceLevel;
            }
            var rc = RadioClient.I;
            if (rc != null)
            {
                o.radioGranted = rc.Granted;
                o.radioReceiving = rc.ReceivingNow;
                o.radioDeadPress = rc.DeadPress;
                o.squelchCount = rc.SquelchCount;
                o.busyCount = rc.BusyCount;
                o.lastTxResult = rc.LastResult.HasValue ? rc.LastResult.Value.ToString() : "";
            }
            o.relayCount = RadioNet.RadioRelayCount;
            o.voiceState = VoiceService.I != null ? VoiceService.I.State.ToString() : "";
            var door = VoiceRouter.OfficeDoor;
            o.officeDoorAcousticOpen = door != null && door.AcousticOpen;
            var cm = ConnectionManager.I;
            o.usingRelay = cm != null && cm.UsingRelay;
            o.joinCode = cm != null ? cm.JoinCode : "";
            o.otherHasTap = local != null && local.Other != null && local.Other.remoteVoice != null && local.Other.remoteVoice.HasTap;
            o.vivoxParticipants = VoiceService.I != null ? VoiceService.I.RemoteParticipants : 0;
            return o;
        }
    }

    /// <summary>Host ↔ client plumbing for automated MPPM tests (lives on the NetState object).</summary>
    public class AutoTestNet : NetSingleton<AutoTestNet>
    {
        readonly Dictionary<int, Observation> m_Reports = new Dictionary<int, Observation>();
        int m_NextRequest = 1;

        /// <summary>Client side: apply scripted input.</summary>
        [Rpc(SendTo.NotServer)]
        public void ClientInputRpc(bool active, Vector2 move, bool ptt, bool headDown, bool eyes, bool testTalk)
        {
            ApplyLocalInput(active, move, ptt, headDown, eyes, testTalk);
        }

        public static void ApplyLocalInput(bool active, Vector2 move, bool ptt, bool headDown, bool eyes, bool testTalk)
        {
            var local = PlayerNet.Local;
            if (local == null || local.inputs == null) return;
            local.inputs.Auto = new PlayerInputs.AutoInput { Active = active, Move = move, Ptt = ptt, HeadDown = headDown, EyesClosed = eyes };
            local.SetFlag(PlayerNet.Flags.TestTalk, testTalk);
        }

        [Rpc(SendTo.NotServer)]
        void RequestReportRpc(int requestId)
        {
            var o = Observation.Capture();
            ReportRpc(requestId, JsonUtility.ToJson(o));
        }

        [Rpc(SendTo.Server)]
        void ReportRpc(int requestId, string json)
        {
            m_Reports[requestId] = JsonUtility.FromJson<Observation>(json);
        }

        /// <summary>Host: ask the client for its observation; returns the request id to poll.</summary>
        public int AskClient()
        {
            int id = m_NextRequest++;
            RequestReportRpc(id);
            return id;
        }

        public bool TryGetReport(int id, out Observation o) => m_Reports.TryGetValue(id, out o);

        /// <summary>Client side: stream the synthetic test voice into its own Vivox transmission.</summary>
        [Rpc(SendTo.NotServer)]
        public void ClientInjectRpc(bool on)
        {
            if (on) VoiceService.I?.StartInjection(VoiceService.TestVoiceWavPath);
            else VoiceService.I?.StopInjection();
        }

        /// <summary>Client side: set a state flag that is not driven by held input (e.g. flashlight on).</summary>
        [Rpc(SendTo.NotServer)]
        public void ClientSetFlagRpc(PlayerNet.Flags flag, bool on)
        {
            PlayerNet.Local?.SetFlag(flag, on);
        }

        [Rpc(SendTo.NotServer)]
        public void ClientLookRpc(float yaw, float pitch)
        {
            var local = PlayerNet.Local;
            if (local != null && local.motor != null) local.motor.SetLook(yaw, pitch);
        }

        /// <summary>Client side: save a screenshot of its game view (visual checks of the second instance).</summary>
        [Rpc(SendTo.NotServer)]
        public void ClientCaptureRpc(string fileName)
        {
            CaptureLocal(fileName);
        }

        /// <summary>
        /// Screen capture (with UI) on the main editor; camera render (world only) on MPPM clones, whose small
        /// game view windows do not produce usable screen captures.
        /// </summary>
        public static void CaptureLocal(string fileName)
        {
            var req = AutoTestRequest.Current;
            string dir = req != null && !string.IsNullOrEmpty(req.reportDir) ? req.reportDir : Application.persistentDataPath;
            dir = System.IO.Path.Combine(dir, "shots");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, fileName);
            var cam = PlayerNet.Local != null ? PlayerNet.Local.cam : null;
            if (InstanceInfo.IsMainEditor || cam == null)
            {
                ScreenCapture.CaptureScreenshot(path);
            }
            else
            {
                var rt = new RenderTexture(1280, 720, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
                Destroy(rt);
            }
            GameLog.Info("AutoTest", "capture " + fileName);
        }

        /// <summary>Client side: set the office/fire door state etc. is server-only; exposed for completeness.</summary>
        [Rpc(SendTo.NotServer)]
        public void ClientLogRpc(string message) => GameLog.Info("AutoTest", message);
    }
}
