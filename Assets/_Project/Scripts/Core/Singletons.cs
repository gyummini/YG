using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Scene-scoped singleton (one per loaded scene, no DontDestroyOnLoad).</summary>
    public abstract class SceneSingleton<T> : MonoBehaviour where T : SceneSingleton<T>
    {
        public static T I { get; private set; }

        protected virtual void Awake()
        {
            if (I != null && I != this)
            {
                Debug.LogWarning($"[NO] duplicate {typeof(T).Name} on {name}");
            }
            I = (T)this;
        }

        protected virtual void OnDestroy()
        {
            if (I == this) I = null;
        }
    }

    /// <summary>Scene-scoped networked singleton (in-scene placed NetworkObject).</summary>
    public abstract class NetSingleton<T> : NetworkBehaviour where T : NetSingleton<T>
    {
        public static T I { get; private set; }

        protected virtual void Awake()
        {
            I = (T)this;
        }

        public override void OnDestroy()
        {
            if (I == this) I = null;
            base.OnDestroy();
        }
    }
}
