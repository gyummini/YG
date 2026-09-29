using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>All entity definitions (Resources/EntityCatalog.asset) plus the manual's tag filter.</summary>
    [CreateAssetMenu(menuName = "NightOffice/Entity Catalog", fileName = "EntityCatalog")]
    public class EntityCatalog : ScriptableObject
    {
        public EntityDefinition[] entities = new EntityDefinition[0];

        static EntityCatalog s_Instance;

        public static EntityCatalog I
        {
            get
            {
                if (s_Instance == null) s_Instance = Resources.Load<EntityCatalog>("EntityCatalog");
                return s_Instance;
            }
        }

        public EntityDefinition Get(EntityId id)
        {
            foreach (var e in entities)
                if (e != null && e.id == id)
                    return e;
            return null;
        }

        public IEnumerable<EntityDefinition> InBundle(BundleId bundle)
        {
            foreach (var e in entities)
                if (e != null && e.bundle == bundle)
                    yield return e;
        }

        /// <summary>First-impression line of a bundle (shown when the bundle is registered on the terminal).</summary>
        public string BundleImpression(BundleId bundle)
        {
            foreach (var e in InBundle(bundle))
                foreach (var c in e.clues)
                    if (c.attr == ClueAttr.FirstImpression)
                        return c.value;
            return bundle.ToString();
        }

        /// <summary>Tag values offered per attribute (in catalog order, without duplicates).</summary>
        public List<KeyValuePair<ClueAttr, List<string>>> TagGroups()
        {
            var order = new List<ClueAttr>();
            var values = new Dictionary<ClueAttr, List<string>>();
            foreach (var e in entities)
            {
                if (e == null) continue;
                foreach (var c in e.clues)
                {
                    if (!values.TryGetValue(c.attr, out var list))
                    {
                        list = new List<string>();
                        values[c.attr] = list;
                        order.Add(c.attr);
                    }
                    if (!list.Contains(c.value)) list.Add(c.value);
                }
            }
            order.Sort();
            var result = new List<KeyValuePair<ClueAttr, List<string>>>();
            foreach (var a in order) result.Add(new KeyValuePair<ClueAttr, List<string>>(a, values[a]));
            return result;
        }

        /// <summary>
        /// Candidates matching every selected tag (one value per attribute). No selection = everything. The manual
        /// never suggests what to check next — it only narrows.
        /// </summary>
        public List<EntityDefinition> Filter(IReadOnlyDictionary<ClueAttr, string> selected)
        {
            var result = new List<EntityDefinition>();
            foreach (var e in entities)
            {
                if (e == null) continue;
                bool ok = true;
                if (selected != null)
                    foreach (var kv in selected)
                        if (!e.Has(kv.Key, kv.Value))
                        {
                            ok = false;
                            break;
                        }
                if (ok) result.Add(e);
            }
            return result;
        }
    }
}
