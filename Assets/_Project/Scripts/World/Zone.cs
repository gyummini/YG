using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// An acoustic space (room, corridor, stair core, elevator cab). Voices and sounds travel freely inside
    /// a zone and between zones joined by open portals; a closed door cuts them.
    /// </summary>
    public class Zone : MonoBehaviour
    {
        public int zoneId;
        public ZoneType type;
        public int floor = 1;
        public string label;
        [Tooltip("Boxes in world space, or in local space when isDynamic (elevator cab).")]
        public Bounds[] boxes = new Bounds[0];
        public bool isDynamic;

        public bool Contains(Vector3 p)
        {
            if (isDynamic) p = transform.InverseTransformPoint(p);
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                if (p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.y && p.y <= b.max.y && p.z >= b.min.z && p.z <= b.max.z)
                    return true;
            }
            return false;
        }

        public float Volume
        {
            get
            {
                float v = 0f;
                foreach (var b in boxes) v += b.size.x * b.size.y * b.size.z;
                return v;
            }
        }

        public Vector3 Center
        {
            get
            {
                if (boxes.Length == 0) return transform.position;
                var c = boxes[0].center;
                return isDynamic ? transform.TransformPoint(c) : c;
            }
        }

        public override string ToString() => string.IsNullOrEmpty(label) ? name : label;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            if (isDynamic) Gizmos.matrix = transform.localToWorldMatrix;
            foreach (var b in boxes) Gizmos.DrawWireCube(b.center, b.size);
        }
#endif
    }

    public interface IAcousticGate
    {
        /// <summary>True while sound passes (door visually not fully shut).</summary>
        bool AcousticOpen { get; }
    }
}
