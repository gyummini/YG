using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Local radio handset: push-to-talk, the transmitter's clicks/busy tone/dead static, the receiver's open
    /// click + hiss + "치직" squelch at the end, and relaying nearby game sounds while transmitting.
    /// </summary>
    public class RadioClient : SceneSingleton<RadioClient>
    {
        bool m_WasHeld;
        bool m_Granted;
        bool m_DeadPress;
        bool m_RxActive;
        AudioSource m_Hiss;
        AudioSource m_DeadStatic;

        public bool Granted => m_Granted;
        public bool ReceivingNow => m_RxActive;
        public bool DeadPress => m_DeadPress && m_WasHeld;
        public RadioTxResult? LastResult { get; private set; }
        public float LastResultTime { get; private set; } = -10f;
        public int SquelchCount { get; private set; }
        public int BusyCount { get; private set; }

        void OnEnable()
        {
            RadioNet.LocalTxResult += OnResult;
            AudioService.LocalAudible += OnLocalAudible;
        }

        void OnDisable()
        {
            RadioNet.LocalTxResult -= OnResult;
            AudioService.LocalAudible -= OnLocalAudible;
        }

        void Start()
        {
            if (AudioService.I == null) return;
            m_Hiss = AudioService.I.CreateLoop(SfxId.RadioHissLoop, transform, 0f, "Radio");
            m_DeadStatic = AudioService.I.CreateLoop(SfxId.RadioDeadStatic, transform, 0f, "Radio");
            m_Hiss.priority = 10;
            m_DeadStatic.priority = 10;
        }

        void Update()
        {
            var local = PlayerNet.Local;
            var radio = RadioNet.I;
            if (local == null || radio == null || !radio.IsSpawned)
            {
                m_WasHeld = m_Granted = m_DeadPress = false;
                SetLoop(m_Hiss, 0f);
                SetLoop(m_DeadStatic, 0f);
                m_RxActive = false;
                return;
            }

            bool held = local.inputs != null && local.inputs.PttHeld && !local.Vanished.Value;
            if (held && !m_WasHeld) OnPress(local);
            if (!held && m_WasHeld) OnRelease();
            m_WasHeld = held;
            local.SetFlag(PlayerNet.Flags.PttHeld, held);

            ulong me = local.OwnerClientId;
            if (m_Granted && radio.Transmitter.Value != me && Time.time - LastResultTime > 0.5f) m_Granted = false;

            float bus = AudioService.I != null ? AudioService.I.RadioBus : 1f;
            SetLoop(m_DeadStatic, held && m_DeadPress ? 0.55f * bus : 0f);
            UpdateReceive(local, radio, bus);
        }

        void OnPress(PlayerNet local)
        {
            if (!RadioLink.IsUp(local))
            {
                m_DeadPress = true; // 관리사무소 주변: 눌러도 잡음뿐, 아무것도 나가지 않는다
                return;
            }
            m_DeadPress = false;
            RadioNet.I.RequestTxRpc();
        }

        void OnRelease()
        {
            var radio = RadioNet.I;
            bool wasOnAir = m_Granted || (radio != null && PlayerNet.Local != null && radio.Transmitter.Value == PlayerNet.Local.OwnerClientId);
            if (wasOnAir) AudioService.I?.Play2D(SfxId.RadioRelease, 0.7f);
            radio?.ReleaseTxRpc();
            m_Granted = false;
            m_DeadPress = false;
        }

        void OnResult(RadioTxResult r)
        {
            LastResult = r;
            LastResultTime = Time.time;
            switch (r)
            {
                case RadioTxResult.Granted:
                    if (m_WasHeld)
                    {
                        m_Granted = true;
                        AudioService.I?.Play2D(SfxId.RadioKeyUp, 0.6f);
                    }
                    else
                    {
                        RadioNet.I?.ReleaseTxRpc();
                    }
                    break;
                case RadioTxResult.Busy:
                case RadioTxResult.Collision:
                    BusyCount++;
                    AudioService.I?.Play2D(SfxId.RadioBusy, 0.8f);
                    break;
            }
        }

        void UpdateReceive(PlayerNet local, RadioNet radio, float bus)
        {
            bool active = false;
            ulong tx = radio.Transmitter.Value;
            var map = ZoneMap.I;
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p == local || p.Vanished.Value) continue;
                if (!radio.IsTransmittingWithTail(p.OwnerClientId)) continue;
                if (!RadioLink.IsUp(local) || !RadioLink.IsUp(p)) continue;
                if (map != null && map.Connected(local.Zone, p.Zone)) continue;
                active = true;
            }

            if (active && !m_RxActive) AudioService.I?.Play2D(SfxId.RadioRxOpen, 0.7f);
            if (!active && m_RxActive)
            {
                AudioService.I?.Play2D(SfxId.RadioSquelch, 0.9f);
                SquelchCount++;
            }
            m_RxActive = active;
            var s = GameSettings.I.voice;
            SetLoop(m_Hiss, active ? (s.radioHissVolume + radio.Noise.Value * 0.6f) * bus : 0f);
        }

        static void SetLoop(AudioSource src, float vol)
        {
            if (src != null) src.volume = vol;
        }

        void OnLocalAudible(SfxId id, Vector3 pos, float volume, SfxFlags flags)
        {
            var local = PlayerNet.Local;
            var radio = RadioNet.I;
            if (local == null || radio == null || !radio.IsSpawned) return;
            ulong me = local.OwnerClientId;
            bool onAir = radio.Transmitter.Value == me && RadioLink.IsUp(local);
            bool forced = radio.ForcedRelayFrom.Value == me;
            if (!onAir && !forced) return;
            if ((flags & SfxFlags.Self) == 0 && Vector3.Distance(local.HeadPosition, pos) > GameSettings.I.voice.radioSfxPickupRadius) return;
            radio.RelaySfxRpc(id, volume);
        }
    }
}
