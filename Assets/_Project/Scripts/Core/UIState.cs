using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>Which UI layers currently own the mouse/keyboard (menu, terminal, fax, pause).</summary>
    public static class UIState
    {
        static readonly HashSet<string> s_Blockers = new HashSet<string>();

        public static bool BlocksGameplay => s_Blockers.Count > 0;
        public static bool TextInputFocused { get; set; }

        public static void Push(string key)
        {
            s_Blockers.Add(key);
            Apply();
        }

        public static void Pop(string key)
        {
            s_Blockers.Remove(key);
            Apply();
        }

        public static bool Has(string key) => s_Blockers.Contains(key);

        public static void Apply()
        {
            bool free = BlocksGameplay || PlayerNet.Local == null;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        public static string Describe() => string.Join(",", s_Blockers);
    }
}
