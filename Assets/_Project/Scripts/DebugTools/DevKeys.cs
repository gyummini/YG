using UnityEngine;

namespace NightOffice
{
    /// <summary>F1 debug overlay, F8 synthetic "test voice" (so voice rules can be heard without a partner talking).</summary>
    public class DevKeys : MonoBehaviour
    {
        public DebugOverlay overlay;

        void Start()
        {
            if (overlay != null) overlay.visible = GameSettings.I.debug.showOverlayOnStart;
        }

        void Update()
        {
            var local = PlayerNet.Local;
            if (local == null || local.inputs == null) return;
            if (local.inputs.DebugPressed && overlay != null) overlay.visible = !overlay.visible;
            if (local.inputs.TestTalkPressed && GameSettings.I.debug.allowTestVoice)
            {
                bool on = !local.Has(PlayerNet.Flags.TestTalk);
                local.SetFlag(PlayerNet.Flags.TestTalk, on);
                GameLog.Info("Dev", "가짜 목소리 " + (on ? "켬" : "끔"));
            }
        }
    }
}
