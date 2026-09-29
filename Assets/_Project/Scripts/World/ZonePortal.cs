using UnityEngine;

namespace NightOffice
{
    /// <summary>Connection between two zones: a doorway (gated by a door) or an open passage.</summary>
    public class ZonePortal : MonoBehaviour
    {
        public Zone a;
        public Zone b;
        [Tooltip("Door that gates this portal. Leave empty for an always-open passage.")]
        public MonoBehaviour gate;

        IAcousticGate m_Gate;
        bool m_Resolved;

        /// <summary>Code-assigned gate (tests, portals built at runtime); replaces the serialized one.</summary>
        public IAcousticGate Gate
        {
            set
            {
                m_Gate = value;
                m_Resolved = true;
            }
        }

        public bool IsOpen
        {
            get
            {
                if (!m_Resolved)
                {
                    m_Gate = gate as IAcousticGate;
                    m_Resolved = true;
                }
                return m_Gate == null || m_Gate.AcousticOpen;
            }
        }

        public Zone Other(Zone z) => z == a ? b : a;
    }
}
