using System.Text;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Developer overlay (F1): zones, voice routes, radio, ducking, elevator, bundles.</summary>
    public class DebugOverlay : MonoBehaviour
    {
        public bool visible;
        readonly StringBuilder m_Sb = new StringBuilder();
        GUIStyle m_Style;

        /// <summary>Other systems (entities, night) append their own lines.</summary>
        public static event System.Action<StringBuilder> Collect;

        void OnGUI()
        {
            if (!visible) return;
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                m_Style.normal.textColor = new Color(0.8f, 1f, 0.8f);
            }
            m_Sb.Clear();
            var local = PlayerNet.Local;
            m_Sb.AppendLine($"<b>NightOffice debug</b>  {InstanceInfo.ShortName}  tags=[{string.Join(",", InstanceInfo.Tags)}]  ui=[{UIState.Describe()}]");
            if (local != null)
            {
                var z = local.Zone;
                m_Sb.AppendLine($"me: client={local.OwnerClientId} role={local.Role} zone={(z != null ? z.label : "-")} floor={local.Floor} pos={local.transform.position:F1}");
                m_Sb.AppendLine($"flags={(PlayerNet.Flags)local.NetFlags.Value} radioUp={RadioLink.IsUp(local)} doorDist={BuildingLayout.DistanceToOfficeDoor(local.transform.position):F1}");
            }
            foreach (var p in PlayerNet.All)
            {
                if (p == null || p == local || p.remoteVoice == null) continue;
                var rv = p.remoteVoice;
                m_Sb.AppendLine($"voice[{p.OwnerClientId}] {rv.Route} tap={rv.HasTap} lvl={rv.Level:0.000} zone={(p.Zone != null ? p.Zone.label : "-")}");
            }
            var radio = RadioNet.I;
            if (radio != null && radio.IsSpawned)
            {
                var rc = RadioClient.I;
                m_Sb.AppendLine($"radio tx={(radio.Transmitter.Value == RadioNet.None ? "-" : radio.Transmitter.Value.ToString())} noise={radio.Noise.Value:0.00} granted={rc?.Granted} rx={rc?.ReceivingNow} relays={RadioNet.RadioRelayCount} squelch={rc?.SquelchCount} busy={rc?.BusyCount}");
            }
            var duck = Ducker.I;
            if (duck != null) m_Sb.AppendLine($"duck {duck.GainDb:0.0}dB voiceLvl={duck.VoiceLevel:0.000}");
            var vs = VoiceService.I;
            if (vs != null) m_Sb.AppendLine($"vivox {vs.State} ch={vs.Channel} {vs.LastError}");
            var el = Elevator.I;
            if (el != null && el.IsSpawned) m_Sb.AppendLine($"elevator y={el.CabY:0.00} rest={el.RestFloor} dir={el.Direction} door={el.DoorState} occ={el.Occupancy}");
            if (GameClock.I != null && GameClock.I.IsSpawned) m_Sb.AppendLine($"clock {GameClock.I.Text} phase={(NightDirector.I != null ? NightDirector.I.Phase.Value.ToString() : "-")}");
            Collect?.Invoke(m_Sb);
            m_Sb.AppendLine("--- log");
            int n = 0;
            var entries = new System.Collections.Generic.List<GameLog.Entry>(GameLog.Entries);
            for (int i = entries.Count - 1; i >= 0 && n < 10; i--, n++)
                m_Sb.AppendLine($"[{entries[i].Tag}] {entries[i].Message}");

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(8, 8, 900, 20 + 16 * CountLines()), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14, 12, 890, 2000), m_Sb.ToString(), m_Style);
        }

        int CountLines()
        {
            int c = 1;
            for (int i = 0; i < m_Sb.Length; i++)
                if (m_Sb[i] == '\n')
                    c++;
            return c;
        }
    }
}
