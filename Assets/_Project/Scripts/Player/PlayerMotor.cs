using UnityEngine;

namespace NightOffice
{
    /// <summary>Owner-only first-person controller: walk, look, head down (floor-only view), eyes closed, ride the elevator.</summary>
    public class PlayerMotor : MonoBehaviour
    {
        public PlayerNet net;
        const float Gravity = -18f;

        float m_Yaw;
        float m_Pitch;
        float m_VerticalVel;
        bool m_HeadDownToggle;
        bool m_EyesToggle;
        Vector3 m_LastPos;

        public bool HeadDown { get; private set; }
        public bool EyesClosed { get; private set; }
        public float Speed { get; private set; }
        public bool Seated { get; set; }
        public float Yaw => m_Yaw;
        public float PitchDeg => m_Pitch;

        public static float SensitivityScale
        {
            get => PlayerPrefs.GetFloat("look.sens", 1f);
            set => PlayerPrefs.SetFloat("look.sens", Mathf.Clamp(value, 0.2f, 3f));
        }

        CharacterController Cc => net.cc;

        public void Teleport(Vector3 position, float yaw)
        {
            if (Cc != null) Cc.enabled = false;
            m_Yaw = yaw;
            var rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            // as a teleport, so the other side jumps too instead of sliding across the building (and through the office)
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt != null && nt.IsSpawned && nt.CanCommitToTransform) nt.Teleport(position, rotation, transform.localScale);
            else transform.SetPositionAndRotation(position, rotation);
            m_VerticalVel = 0f;
            m_LastPos = position;
            if (Cc != null && net.IsOwner) Cc.enabled = true;
        }

        public void SetLook(float yaw, float pitch)
        {
            m_Yaw = yaw;
            m_Pitch = pitch;
        }

        void OnEnable()
        {
            m_Yaw = transform.eulerAngles.y;
            m_LastPos = transform.position;
        }

        void Update()
        {
            if (net == null || !net.IsOwner || !net.IsSpawned) return;
            var s = GameSettings.I.player;
            var inp = net.inputs;
            bool vanished = net.Vanished.Value;
            bool blocked = vanished || Seated;

            // --- look
            if (!vanished)
            {
                var look = inp.Look * s.lookSensitivity * SensitivityScale;
                m_Yaw += look.x;
                m_Pitch -= look.y;
            }

            // --- head down / eyes closed
            if (PlayerPrefs.GetInt("headdown.toggle", s.headDownToggle ? 1 : 0) == 1)
            {
                if (inp.HeadDownPressed) m_HeadDownToggle = !m_HeadDownToggle;
                HeadDown = !vanished && m_HeadDownToggle;
            }
            else HeadDown = !vanished && inp.HeadDownHeld;

            if (s.eyesClosedToggle)
            {
                if (inp.EyesPressed) m_EyesToggle = !m_EyesToggle;
                EyesClosed = !vanished && m_EyesToggle;
            }
            else EyesClosed = !vanished && inp.EyesHeld;

            // Head down limits the view to the floor; the head eases down instead of snapping.
            float minPitch = HeadDown ? s.headDownPitch - s.headDownRange : -85f;
            if (HeadDown && m_Pitch < minPitch) m_Pitch = Mathf.MoveTowards(m_Pitch, minPitch, 420f * Time.deltaTime);
            else m_Pitch = Mathf.Clamp(m_Pitch, minPitch, 88f);
            m_Pitch = Mathf.Min(m_Pitch, 88f);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (net.head != null) net.head.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);

            // --- move
            Vector3 wish = Vector3.zero;
            if (!blocked)
            {
                var mv = Vector2.ClampMagnitude(inp.Move, 1f);
                wish = (transform.right * mv.x + transform.forward * mv.y) * s.walkSpeed;
            }

            var cc = Cc;
            if (cc != null && cc.enabled)
            {
                if (cc.isGrounded && m_VerticalVel < 0f) m_VerticalVel = -2f;
                m_VerticalVel += Gravity * Time.deltaTime;

                Vector3 platform = Vector3.zero;
                var elev = Elevator.I;
                if (elev != null && elev.CabContains(transform.position + Vector3.up * 0.4f))
                {
                    platform.y = elev.CabDeltaY;
                    if (Mathf.Abs(elev.CabDeltaY) > 0.0001f && m_VerticalVel < 0f) m_VerticalVel = -2f;
                }

                var motion = (wish + Vector3.up * m_VerticalVel) * Time.deltaTime + platform;
                cc.Move(motion);
            }

            // --- derived state
            var p = transform.position;
            var d = p - m_LastPos;
            d.y = 0f;
            float inst = Time.deltaTime > 0f ? d.magnitude / Time.deltaTime : 0f;
            if (inst > 12f) inst = 0f; // teleport
            Speed = Mathf.Lerp(Speed, inst, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            m_LastPos = p;

            bool moving = Speed > s.movingThreshold;
            net.SetFlag(PlayerNet.Flags.Moving, moving);
            net.SetFlag(PlayerNet.Flags.HeadDown, HeadDown);
            net.SetFlag(PlayerNet.Flags.EyesClosed, EyesClosed);
            if (Mathf.Abs(net.Pitch.Value - m_Pitch) > 0.25f) net.Pitch.Value = m_Pitch;
            if (AudioService.I != null) AudioService.I.LocalMoving = moving;
        }
    }
}
