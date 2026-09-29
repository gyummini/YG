using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>민원 문구. {unit} = 호수, {floor} = 층. Each line hints at one bundle (출동 전 브리핑).</summary>
    [CreateAssetMenu(menuName = "NightOffice/Complaint Catalog", fileName = "ComplaintCatalog")]
    public class ComplaintCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public BundleId bundle;
            public ComplaintTask task;
            [TextArea] public string text;
        }

        public Entry[] entries = new Entry[0];

        static ComplaintCatalog s_I;

        public static ComplaintCatalog I
        {
            get
            {
                if (s_I == null) s_I = Resources.Load<ComplaintCatalog>("ComplaintCatalog");
                return s_I;
            }
        }

        public List<int> For(BundleId bundle)
        {
            var list = new List<int>();
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].bundle == bundle)
                    list.Add(i);
            return list;
        }

        public string Text(int template, int unit)
        {
            if (template < 0 || template >= entries.Length || entries[template] == null) return "";
            return entries[template].text.Replace("{unit}", unit.ToString()).Replace("{floor}", (unit / 100).ToString());
        }
    }
}
