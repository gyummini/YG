using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    public enum FollowMode : byte
    {
        None = 0,
        Follower = 1, // 뒷사람: steps exactly with the field's, an invisible body that casts a shadow
        Echo = 2,     // 울림: an off-beat echo in the stairwell that fades one beat after the field stops
    }

    /// <summary>
    /// 묶음 D "뒤에서 발소리가 따라온다". The server places this object (behind the field, or in the stairwell for the echo);
    /// every client plays its steps from the field player's own footsteps, so they start and stop with the field's
    /// and are relayed on the radio like any other sound near the field. 뒷사람's body renders shadows only.
    /// </summary>
    public class Follower : NetSingleton<Follower>
    {
        [Tooltip("Shadow-only renderers (뒷사람's second shadow in the flashlight beam).")]
        public Renderer[] shadowBody = new Renderer[0];
        public Transform headPoint;
        public Vector3 parking = new Vector3(0f, -40f, 0f);

        public readonly NetworkVariable<FollowMode> Mode = new NetworkVariable<FollowMode>(FollowMode.None);

        float m_LastFieldStep = -10f;
        bool m_TailArmed;

        /// <summary>Local counters for automated tests: follower / echo steps actually played on this client.</summary>
        public static int FollowerSteps, EchoSteps;
        public static float LastFollowerStepAt = -10f, LastEchoStepAt = -10f, LastFieldStepAt = -10f;

        public Vector3 HeadPosition => headPoint != null ? headPoint.position : transform.position + Vector3.up * 1.6f;

        void OnEnable() => Footsteps.Stepped += OnStepped;

        void OnDisable() => Footsteps.Stepped -= OnStepped;

        public override void OnNetworkSpawn()
        {
            if (IsServer) ServerSet(FollowMode.None);
        }

        void OnStepped(PlayerNet p, Vector3 feet, bool stair)
        {
            if (p == null || p.Role != Role.Field || !IsSpawned) return;
            LastFieldStepAt = Time.time;
            switch (Mode.Value)
            {
                case FollowMode.Follower:
                    StartCoroutine(StepAfter(SfxId.StepFollower, 0.04f, 1f));
                    break;
                case FollowMode.Echo:
                    StartCoroutine(StepAfter(SfxId.StepEcho, GameSettings.I.entities.echoDelaySec, 0.9f));
                    m_LastFieldStep = Time.time;
                    m_TailArmed = true;
                    break;
            }
        }

        IEnumerator StepAfter(SfxId id, float delay, float volume)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (Mode.Value == FollowMode.None) yield break;
            AudioService.I?.PlayAt(id, transform.position + Vector3.up * 0.05f, volume);
            if (id == SfxId.StepEcho)
            {
                EchoSteps++;
                LastEchoStepAt = Time.time;
            }
            else
            {
                FollowerSteps++;
                LastFollowerStepAt = Time.time;
            }
        }

        void Update()
        {
            bool body = Mode.Value == FollowMode.Follower;
            foreach (var r in shadowBody)
                if (r != null && r.enabled != body)
                    r.enabled = body;

            // 울림: the field stopped (no step for a while) → one more echo, one beat late, then quiet
            if (Mode.Value == FollowMode.Echo && m_TailArmed && Time.time - m_LastFieldStep > 0.45f)
            {
                m_TailArmed = false;
                var s = GameSettings.I.entities;
                float at = m_LastFieldStep + s.echoDelaySec + s.echoTailSec;
                StartCoroutine(StepAfter(SfxId.StepEcho, Mathf.Max(0f, at - Time.time), 0.55f));
            }
            if (Mode.Value != FollowMode.Echo) m_TailArmed = false;
        }

        // ---------------------------------------------------------------- server API
        public void ServerSet(FollowMode mode)
        {
            if (!IsServer) return;
            Mode.Value = mode;
            if (mode == FollowMode.None) transform.position = parking;
        }

        public void ServerPlace(Vector3 feet, Vector3 faceToward)
        {
            if (!IsServer) return;
            transform.position = feet;
            var flat = faceToward - feet;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat);
        }

        /// <summary>뒷사람 leaves: a few steps walking away (played on every client), then nothing.</summary>
        public void ServerWalkAway(Vector3 awayDir)
        {
            if (!IsServer || Mode.Value != FollowMode.Follower) return;
            StartCoroutine(WalkAway(awayDir));
        }

        IEnumerator WalkAway(Vector3 dir)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : -transform.forward;
            var pos = transform.position;
            for (int i = 0; i < 5; i++)
            {
                pos += dir * 0.75f;
                StepRpc(pos, 1f - i * 0.17f);
                yield return new WaitForSeconds(0.42f);
            }
            ServerSet(FollowMode.None);
        }

        [Rpc(SendTo.Everyone)]
        void StepRpc(Vector3 pos, float volume)
        {
            AudioService.I?.PlayAt(SfxId.StepFollower, pos + Vector3.up * 0.05f, volume);
        }
    }
}
