using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Night clock: 00:00 → 04:00 over GameSettings.night.realSecondsPerNight, shared through server time.</summary>
    public class GameClock : NetSingleton<GameClock>
    {
        public const float NightMinutes = 240f;

        public readonly NetworkVariable<double> StartServerTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<bool> Running = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<float> FrozenMinutes = new NetworkVariable<float>(0f);

        public static float MinutesNow => I != null ? I.Minutes : 0f;

        public float Minutes
        {
            get
            {
                if (!Running.Value || NetworkManager == null) return FrozenMinutes.Value;
                double elapsed = NetworkManager.ServerTime.Time - StartServerTime.Value;
                float m = (float)(elapsed / Mathf.Max(1f, GameSettings.I.night.realSecondsPerNight) * NightMinutes);
                return Mathf.Clamp(m, 0f, NightMinutes);
            }
        }

        public string Text => Format(Minutes);

        public static string Format(float minutes)
        {
            int m = Mathf.FloorToInt(minutes);
            return $"{m / 60:00}:{m % 60:00}";
        }

        public void ServerStart()
        {
            if (!IsServer) return;
            StartServerTime.Value = NetworkManager.ServerTime.Time;
            FrozenMinutes.Value = 0f;
            Running.Value = true;
        }

        public void ServerStop()
        {
            if (!IsServer) return;
            FrozenMinutes.Value = Minutes;
            Running.Value = false;
        }

        /// <summary>Tests / debug: jump the running clock to a time of night.</summary>
        public void ServerSetMinutes(float minutes)
        {
            if (!IsServer || !Running.Value) return;
            double sec = minutes / NightMinutes * Mathf.Max(1f, GameSettings.I.night.realSecondsPerNight);
            StartServerTime.Value = NetworkManager.ServerTime.Time - sec;
        }

        public void ServerReset()
        {
            if (!IsServer) return;
            Running.Value = false;
            FrozenMinutes.Value = 0f;
        }
    }
}
