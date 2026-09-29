using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace NightOffice
{
    /// <summary>
    /// The tall figure (묶음 A: 키다리 / 배웅꾼 look the same at first). Server-driven NavMesh walker, replicated with a
    /// server-authoritative NetworkTransform. 키다리 shows the extra finger on each hand (6 fingers) and makes no
    /// footsteps; 배웅꾼 has 5 fingers and heavy footsteps that every client plays (so they are relayed on the radio).
    /// </summary>
    public class TallFigure : NetSingleton<TallFigure>
    {
        public NavMeshAgent agent;
        public Renderer[] bodyRenderers = new Renderer[0];
        public Renderer[] extraFingers = new Renderer[0];
        public Transform headPoint;
        public Transform handLeft;
        public Transform handRight;
        [Tooltip("Where the figure waits while hidden (off the navmesh).")]
        public Vector3 parking = new Vector3(0f, -30f, 0f);

        public readonly NetworkVariable<bool> Shown = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<EntityId> Kind = new NetworkVariable<EntityId>(EntityId.None);

        Vector3 m_LastPos;
        float m_StepAccum;
        bool m_WasShown;

        public Vector3 HeadPosition => headPoint != null ? headPoint.position : transform.position + Vector3.up * 2.35f;
        public Vector3 Feet => transform.position;

        protected override void Awake()
        {
            base.Awake();
            if (agent != null) agent.enabled = false;
            ApplyVisibility(false, EntityId.None);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) ServerHide();
        }

        void Update()
        {
            bool shown = Shown.Value;
            if (shown != m_WasShown)
            {
                m_LastPos = transform.position;
                m_StepAccum = 0f;
                m_WasShown = shown;
            }
            ApplyVisibility(shown, Kind.Value);
            if (!shown) return;

            // 배웅꾼 footsteps from the replicated motion (every client, zone-gated and radio-relayed by AudioService)
            var d = transform.position - m_LastPos;
            d.y = 0f;
            m_LastPos = transform.position;
            if (Kind.Value != EntityId.Escort || d.magnitude > 1.5f) return;
            m_StepAccum += d.magnitude;
            if (m_StepAccum >= 0.85f)
            {
                m_StepAccum = 0f;
                AudioService.I?.PlayAt(SfxId.StepHeavy, Feet + Vector3.up * 0.05f, 1f);
            }
        }

        void ApplyVisibility(bool shown, EntityId kind)
        {
            foreach (var r in bodyRenderers)
                if (r != null && r.enabled != shown) r.enabled = shown;
            bool six = shown && kind == EntityId.TallOne;
            foreach (var r in extraFingers)
                if (r != null && r.enabled != six) r.enabled = six;
        }

        // ---------------------------------------------------------------- server API
        public void ServerAppear(EntityId kind, Vector3 position, Vector3 lookAt)
        {
            if (!IsServer) return;
            Kind.Value = kind;
            transform.position = position;
            var flat = lookAt - position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat);
            if (agent != null)
            {
                agent.enabled = true;
                agent.Warp(position);
                agent.isStopped = false;
            }
            Shown.Value = true;
        }

        public void ServerHide()
        {
            if (!IsServer) return;
            Shown.Value = false;
            if (agent != null) agent.enabled = false;
            transform.position = parking;
        }

        public void ServerMoveTo(Vector3 target, float speed, float stopDistance)
        {
            if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.speed = speed;
            agent.stoppingDistance = stopDistance;
            agent.isStopped = false;
            agent.SetDestination(target);
        }

        public void ServerStop()
        {
            if (!IsServer || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
        }

        public void ServerFace(Vector3 point)
        {
            if (!IsServer) return;
            var flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 240f * Time.deltaTime);
        }

        public bool Arrived => agent == null || !agent.enabled || !agent.isOnNavMesh ||
                               (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f);

        /// <summary>Everyone: a sound at the figure's head (목 꺾이는 소리, 숨소리...).</summary>
        [Rpc(SendTo.Everyone)]
        public void PlayAtHeadRpc(SfxId id, float volume)
        {
            AudioService.I?.PlayAt(id, HeadPosition, volume);
        }
    }
}
