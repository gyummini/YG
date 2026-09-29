using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NightOffice
{
    /// <summary>
    /// The elevator mirror (local to each client). A camera at the viewer's eye mirrored through the glass looks back
    /// into the cab with an off-axis frustum whose near plane is the glass itself, so the wall behind the mirror is
    /// clipped and the image lands exactly on the quad (the shader flips it left-right). It also sees the MirrorOnly
    /// layer (동승자) and the LocalBody layer (your own body). Renders only while the local camera is near and in
    /// front of the glass.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class MirrorView : MonoBehaviour
    {
        [Tooltip("Render texture width; height follows the quad's aspect.")]
        public int resolution = 512;
        public float activeDistance = 4.5f;
        public LayerMask cullingMask = ~0;

        Camera m_Cam;
        RenderTexture m_Rt;
        Renderer m_Renderer;
        Material m_Mat;
        Camera m_Viewer;
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");

        /// <summary>For tests: did the mirror render this frame?</summary>
        public bool Active => m_Cam != null && m_Cam.enabled;

        void Awake() => m_Renderer = GetComponent<Renderer>();

        void OnEnable() => RenderPipelineManager.beginContextRendering += OnBeginContext;

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            if (m_Cam != null) m_Cam.enabled = false;
        }

        void OnDestroy()
        {
            if (m_Cam != null) Destroy(m_Cam.gameObject);
            if (m_Rt != null) m_Rt.Release();
        }

        void Ensure()
        {
            if (m_Cam != null) return;
            var s = transform.lossyScale;
            int h = Mathf.Max(64, Mathf.RoundToInt(resolution * Mathf.Abs(s.y) / Mathf.Max(0.01f, Mathf.Abs(s.x))));
            m_Rt = new RenderTexture(resolution, h, 24, RenderTextureFormat.ARGB32) { name = "ElevatorMirror" };
            var go = new GameObject("MirrorCamera") { hideFlags = HideFlags.DontSave };
            m_Cam = go.AddComponent<Camera>();
            m_Cam.enabled = false;
            m_Cam.targetTexture = m_Rt;
            m_Cam.cullingMask = cullingMask;
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.backgroundColor = Color.black;
            m_Cam.depth = -10f;
            m_Cam.allowHDR = false;
            m_Cam.allowMSAA = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
            m_Mat = m_Renderer.material; // instance
            m_Mat.SetTexture(s_MainTex, m_Rt);
        }

        Camera Viewer()
        {
            var local = PlayerNet.Local;
            if (local != null && local.cam != null && local.cam.isActiveAndEnabled) return local.cam;
            return Camera.main;
        }

        void LateUpdate()
        {
            m_Viewer = Viewer();
            bool on = m_Viewer != null && InFront(m_Viewer.transform.position) &&
                      Vector3.Distance(m_Viewer.transform.position, transform.position) < activeDistance;
            if (on) Ensure();
            if (m_Cam != null && m_Cam.enabled != on) m_Cam.enabled = on;
            if (on) Place();
        }

        /// <summary>The quad's visible face looks along -forward (into the cab).</summary>
        Vector3 Normal => -transform.forward;

        bool InFront(Vector3 p) => Vector3.Dot(p - transform.position, Normal) > 0.02f;

        void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cams)
        {
            if (m_Cam != null && m_Cam.enabled && m_Viewer != null) Place(); // the viewer's final pose this frame
        }

        void Place()
        {
            var n = Normal;
            var c = transform.position;
            var eye = m_Viewer.transform.position;
            float d = Vector3.Dot(eye - c, n);
            if (d <= 0.02f) return;
            var mirroredEye = eye - 2f * d * n;
            m_Cam.transform.SetPositionAndRotation(mirroredEye, Quaternion.LookRotation(n, transform.up));

            // off-axis frustum through the quad's corners; near plane = the glass
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue, z = 0f;
            for (int i = 0; i < 4; i++)
            {
                var corner = transform.TransformPoint(new Vector3(i < 2 ? -0.5f : 0.5f, (i & 1) == 0 ? -0.5f : 0.5f, 0f));
                var lc = m_Cam.transform.InverseTransformPoint(corner);
                xMin = Mathf.Min(xMin, lc.x);
                xMax = Mathf.Max(xMax, lc.x);
                yMin = Mathf.Min(yMin, lc.y);
                yMax = Mathf.Max(yMax, lc.y);
                z = lc.z;
            }
            float near = Mathf.Max(0.01f, z);
            float far = near + 25f;
            m_Cam.nearClipPlane = near;
            m_Cam.farClipPlane = far;
            m_Cam.projectionMatrix = Matrix4x4.Frustum(xMin, xMax, yMin, yMax, near, far);
        }
    }
}
