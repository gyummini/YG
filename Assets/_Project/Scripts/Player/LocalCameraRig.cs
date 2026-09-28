using UnityEngine;

namespace NightOffice
{
    /// <summary>Switches between the menu camera (not connected) and the local player's camera.</summary>
    public static class LocalCameraRig
    {
        public static Camera MenuCamera;

        public static void OnLocalPlayer(PlayerNet p)
        {
            if (MenuCamera != null)
            {
                MenuCamera.enabled = false;
                var l = MenuCamera.GetComponent<AudioListener>();
                if (l != null) l.enabled = false;
            }
            if (AudioService.I != null && p.cam != null) AudioService.I.Listener = p.cam.transform;
            UIState.Apply();
        }

        public static void OnLocalPlayerGone()
        {
            if (MenuCamera != null)
            {
                MenuCamera.enabled = true;
                var l = MenuCamera.GetComponent<AudioListener>();
                if (l != null) l.enabled = true;
                if (AudioService.I != null) AudioService.I.Listener = MenuCamera.transform;
            }
            UIState.Apply();
        }
    }
}
