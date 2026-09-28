using System;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Footsteps for every player instance. Runs on all clients from observed movement, so each client hears
    /// the other player's steps (zone-gated). Raises <see cref="Stepped"/> so followers/echoes can sync.
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        public PlayerNet net;

        /// <summary>(player, feet position, on stairs) on every client for every player's step.</summary>
        public static event Action<PlayerNet, Vector3, bool> Stepped;

        Vector3 m_Last;
        float m_Accum;
        bool m_Init;

        void Update()
        {
            if (net == null || !net.IsSpawned) return;
            var pos = transform.position;
            if (!m_Init)
            {
                m_Last = pos;
                m_Init = true;
                return;
            }
            var d = pos - m_Last;
            m_Last = pos;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > 1.5f || net.Vanished.Value) return; // teleport / gone

            float stride = GameSettings.I.player.footstepStride;
            m_Accum += dist;
            if (m_Accum >= stride)
            {
                m_Accum -= stride;
                Step(pos);
            }
            else if (dist < 0.0001f)
            {
                // standing: the next step lands soon after starting to walk again
                m_Accum = Mathf.Min(m_Accum, stride * 0.55f);
            }
        }

        void Step(Vector3 pos)
        {
            bool stair = net.ZoneType == ZoneType.Stair;
            var id = stair ? SfxId.StepStair : SfxId.StepConcrete;
            bool own = net.IsOwner;
            AudioService.I?.PlayAt(id, pos + Vector3.up * 0.05f, own ? 0.5f : 1f, own ? SfxFlags.Self : SfxFlags.None);
            Stepped?.Invoke(net, pos, stair);
        }
    }
}
