using UnityEngine;

namespace NightOffice
{
    /// <summary>Something the local player can use with E (door handle, card reader, button, panel...).</summary>
    public interface IInteractable
    {
        /// <summary>Prompt shown to the player, or null when not usable right now.</summary>
        string Prompt(PlayerNet player);

        /// <summary>0 = instant, otherwise E must be held this long.</summary>
        float HoldSeconds(PlayerNet player);

        /// <summary>Runs on the local (owning) client; implementations send RPCs as needed.</summary>
        void Interact(PlayerNet player);

        Vector3 InteractPoint { get; }
    }

    /// <summary>Base MonoBehaviour for collider-backed interactables so Interactor can find them.</summary>
    public abstract class InteractableBehaviour : MonoBehaviour, IInteractable
    {
        public abstract string Prompt(PlayerNet player);
        public virtual float HoldSeconds(PlayerNet player) => 0f;
        public abstract void Interact(PlayerNet player);
        public virtual Vector3 InteractPoint => transform.position;
    }
}
