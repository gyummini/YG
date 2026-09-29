using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// One running entity / anomaly / normal situation (server only). Subclasses judge the field player's replicated
    /// state every tick and report mistakes; the director turns them into a warning (first mistake, if the entity
    /// has a warning behaviour) or a vanish.
    /// </summary>
    public abstract class Encounter
    {
        public EntityDefinition Def { get; private set; }
        public EntityId Id => Def != null ? Def.id : EntityId.None;
        public PlayerNet Field { get; private set; }
        public bool Warned { get; private set; }
        public bool Finished { get; private set; }
        public bool Resolved { get; private set; }
        public float Age { get; private set; }
        public string LastMistake { get; private set; }
        public int Mistakes { get; private set; }

        protected EntityDirector Director { get; private set; }
        protected static GameSettings.EntitySettings S => GameSettings.I.entities;

        float m_MistakeCooldownUntil;

        public void Start(EntityDirector director, EntityDefinition def, PlayerNet field)
        {
            Director = director;
            Def = def;
            Field = field;
            OnBegin();
        }

        public void Tick(float dt)
        {
            if (Finished) return;
            Age += dt;
            if (Field == null || !Field.IsSpawned || Field.Vanished.Value)
            {
                Finish(false);
                return;
            }
            OnTick(dt);
        }

        /// <summary>The field broke a rule. Warns once (if the entity has a warning behaviour), then vanishes the field.</summary>
        protected void Mistake(string reason)
        {
            if (Finished || Time.time < m_MistakeCooldownUntil) return;
            m_MistakeCooldownUntil = Time.time + 1.5f;
            Mistakes++;
            LastMistake = reason;
            if (Def.hasWarning && !Warned)
            {
                Warned = true;
                GameLog.Info("Entity", $"{Def.displayName}: 경고 ({reason})");
                OnWarning(reason);
                Director.ServerReport(this, EncounterEvent.Warning, reason);
                return;
            }
            GameLog.Info("Entity", $"{Def.displayName}: 실수 → 사라짐 ({reason})");
            Director.ServerReport(this, EncounterEvent.Vanish, reason);
            Director.ServerVanishField(Field, Def, reason);
            Finish(false);
        }

        protected void Succeed(string how)
        {
            if (Finished) return;
            GameLog.Info("Entity", $"{Def.displayName}: 해결 ({how})");
            Director.ServerReport(this, EncounterEvent.Resolved, how);
            Finish(true);
        }

        /// <summary>Ends the encounter (also used when the bundle is cleared on returning to the office).</summary>
        public void Finish(bool resolved)
        {
            if (Finished) return;
            Finished = true;
            Resolved = resolved;
            OnEnd();
        }

        protected abstract void OnBegin();
        protected abstract void OnTick(float dt);
        protected virtual void OnWarning(string reason) { }
        protected virtual void OnEnd() { }
    }

    public enum EncounterEvent : byte
    {
        Begin = 0,
        Warning = 1,
        Vanish = 2,
        Resolved = 3,
        Cleared = 4,
    }
}
