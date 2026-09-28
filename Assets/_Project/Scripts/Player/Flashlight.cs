using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 손전등: on/off (F), put on the floor / pick up (G). Hand light follows the head; the floor light stays
    /// where it was put down. Casts shadows (뒷사람's second shadow shows up in this light).
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        public PlayerNet net;
        public Light handLight;
        public GameObject floorObject;
        public Light floorLight;

        void Update()
        {
            if (net == null || !net.IsSpawned) return;
            bool dead = net.FlashDead.Value;
            bool on = net.Has(PlayerNet.Flags.FlashOn) && !dead;
            bool onFloor = net.Has(PlayerNet.Flags.FlashOnFloor);
            bool gone = net.Vanished.Value;

            if (handLight != null) handLight.enabled = on && !onFloor && !gone;
            if (floorObject != null)
            {
                if (floorObject.activeSelf != onFloor) floorObject.SetActive(onFloor);
                if (onFloor)
                {
                    floorObject.transform.SetPositionAndRotation(net.FlashFloorPos.Value, Quaternion.Euler(0f, net.FlashFloorYaw.Value, 0f));
                    if (floorLight != null) floorLight.enabled = on;
                }
            }

            if (net.IsOwner) OwnerInput(dead, onFloor);
        }

        void OwnerInput(bool dead, bool onFloor)
        {
            var inp = net.inputs;
            if (inp == null || net.Vanished.Value) return;

            if (inp.FlashPressed)
            {
                AudioService.I?.PlayAt(SfxId.FlashlightClick, net.HeadPosition, 0.8f, SfxFlags.Self);
                if (!dead && (!onFloor || NearFloorLight()))
                    net.SetFlag(PlayerNet.Flags.FlashOn, !net.Has(PlayerNet.Flags.FlashOn));
            }

            if (inp.DropPressed)
            {
                if (!onFloor)
                {
                    var fwd = transform.forward;
                    var pos = transform.position + fwd * 0.45f + Vector3.up * 0.06f;
                    net.FlashFloorPos.Value = pos;
                    net.FlashFloorYaw.Value = transform.eulerAngles.y;
                    net.SetFlag(PlayerNet.Flags.FlashOnFloor, true);
                    AudioService.I?.PlayAt(SfxId.FlashlightDrop, pos, 1f);
                }
                else if (NearFloorLight())
                {
                    net.SetFlag(PlayerNet.Flags.FlashOnFloor, false);
                    AudioService.I?.PlayAt(SfxId.FlashlightClick, net.HeadPosition, 0.6f, SfxFlags.Self);
                }
            }
        }

        bool NearFloorLight()
        {
            var d = net.FlashFloorPos.Value - transform.position;
            d.y = 0f;
            return d.magnitude < 1.6f;
        }

        /// <summary>Is the flashlight beam (in hand or on the floor) currently hitting a point? Used for 손가락 판별/얼굴 비춤.</summary>
        public static bool BeamHits(PlayerNet p, Vector3 point, float maxDistance = 12f)
        {
            if (p == null || p.FlashDead.Value || !p.Has(PlayerNet.Flags.FlashOn)) return false;
            Vector3 origin, dir;
            float halfAngle;
            if (p.Has(PlayerNet.Flags.FlashOnFloor))
            {
                origin = p.FlashFloorPos.Value;
                dir = Quaternion.Euler(0f, p.FlashFloorYaw.Value, 0f) * Vector3.forward;
                halfAngle = 28f;
            }
            else
            {
                origin = p.HeadPosition;
                dir = p.LookDirection;
                halfAngle = 22f;
            }
            var to = point - origin;
            float d = to.magnitude;
            if (d > maxDistance || d < 0.01f) return false;
            return Vector3.Angle(dir, to) <= halfAngle;
        }
    }
}
