using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Finds what the local player can use: first the thing under the crosshair, then (for head-down / eyes
    /// closed use) the nearest usable thing in front of the body. Handles tap and hold interactions.
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        public PlayerNet net;
        public LayerMask mask = ~0;

        public string Prompt { get; private set; }
        public float HoldProgress { get; private set; }
        public IInteractable Current { get; private set; }

        IInteractable m_HoldTarget;
        float m_HoldTime;
        bool m_WaitRelease;
        readonly Collider[] m_Overlap = new Collider[24];

        void Update()
        {
            if (net == null || !net.IsOwner || !net.IsSpawned || net.Vanished.Value || UIState.BlocksGameplay)
            {
                Clear();
                return;
            }

            var inp = net.inputs;
            Current = Find();
            Prompt = Current?.Prompt(net);
            if (Current == null || Prompt == null)
            {
                Clear();
                return;
            }

            float need = Current.HoldSeconds(net);
            if (need <= 0f)
            {
                HoldProgress = 0f;
                if (inp.InteractPressed) Current.Interact(net);
                return;
            }

            if (m_WaitRelease)
            {
                if (!inp.InteractHeld) m_WaitRelease = false;
                HoldProgress = 0f;
                return;
            }

            if (inp.InteractHeld)
            {
                if (m_HoldTarget != Current)
                {
                    m_HoldTarget = Current;
                    m_HoldTime = 0f;
                }
                m_HoldTime += Time.deltaTime;
                HoldProgress = Mathf.Clamp01(m_HoldTime / need);
                if (m_HoldTime >= need)
                {
                    Current.Interact(net);
                    m_HoldTarget = null;
                    m_HoldTime = 0f;
                    m_WaitRelease = true;
                }
            }
            else
            {
                m_HoldTarget = null;
                m_HoldTime = 0f;
                HoldProgress = 0f;
            }
        }

        void Clear()
        {
            Current = null;
            Prompt = null;
            HoldProgress = 0f;
            m_HoldTarget = null;
            m_HoldTime = 0f;
        }

        IInteractable Find()
        {
            float dist = GameSettings.I.player.interactDistance;
            var head = net.cam != null ? net.cam.transform : net.head;
            if (head != null && !net.Has(PlayerNet.Flags.EyesClosed))
            {
                if (Physics.Raycast(head.position, head.forward, out var hit, dist, mask, QueryTriggerInteraction.Collide))
                {
                    var ia = hit.collider.GetComponentInParent<IInteractable>();
                    if (ia != null && ia.Prompt(net) != null) return ia;
                    if (ia == null && !net.Has(PlayerNet.Flags.HeadDown)) return null; // looking at a wall
                }
            }

            // Fallback: nearest usable thing in front of the body (works with the head down).
            var center = transform.position + Vector3.up * 1.0f;
            int n = Physics.OverlapSphereNonAlloc(center, 1.5f, m_Overlap, mask, QueryTriggerInteraction.Collide);
            IInteractable best = null;
            float bestScore = float.MaxValue;
            var fwd = transform.forward;
            for (int i = 0; i < n; i++)
            {
                var ia = m_Overlap[i].GetComponentInParent<IInteractable>();
                if (ia == null || ia.Prompt(net) == null) continue;
                var to = ia.InteractPoint - center;
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang > 65f) continue;
                float score = ang + to.magnitude * 20f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = ia;
                }
            }
            return best;
        }
    }
}
