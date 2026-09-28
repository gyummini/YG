using UnityEngine;

namespace NightOffice
{
    /// <summary>Registers the menu camera as the listener until a local player spawns.</summary>
    [RequireComponent(typeof(Camera))]
    public class MenuCameraRegistrar : MonoBehaviour
    {
        void Start()
        {
            LocalCameraRig.MenuCamera = GetComponent<Camera>();
            if (PlayerNet.Local == null && AudioService.I != null) AudioService.I.Listener = transform;
            UIState.Apply();
        }
    }
}
