using System;
using System.Linq;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Identity of this running instance. In Multiplayer Play Mode every additional Editor gets its own
    /// Authentication profile so two instances on one PC get two different player ids.
    /// </summary>
    public static class InstanceInfo
    {
        static string s_Profile;

        /// <summary>MPPM tags of this instance (empty in builds or the default scenario).</summary>
        public static string[] Tags
        {
            get
            {
#if UNITY_EDITOR
                try
                {
                    var tags = Unity.Multiplayer.PlayMode.CurrentPlayer.Tags;
                    return tags != null ? tags.ToArray() : Array.Empty<string>();
                }
                catch (Exception)
                {
                    return Array.Empty<string>();
                }
#else
                return Array.Empty<string>();
#endif
            }
        }

        public static bool HasTag(string tag) => Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

        public static bool IsMainEditor
        {
            get
            {
#if UNITY_EDITOR
                try
                {
                    return Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
                }
                catch (Exception)
                {
                    return true;
                }
#else
                return false;
#endif
            }
        }

        /// <summary>Stable per-instance profile name for Unity Authentication (max 30 chars, [a-zA-Z0-9_-]).</summary>
        public static string AuthProfile
        {
            get
            {
                if (s_Profile != null) return s_Profile;
                var arg = CommandLineValue("-profile");
                if (!string.IsNullOrEmpty(arg))
                {
                    s_Profile = Sanitize(arg);
                }
                else
                {
#if UNITY_EDITOR
                    // Each MPPM clone runs from its own Library/VP/<id> data path.
                    var h = (uint)Application.dataPath.GetHashCode() ^ (uint)Environment.CurrentDirectory.GetHashCode();
                    s_Profile = IsMainEditor ? "main" : "vp" + (h % 100000);
#else
                    s_Profile = "player" + (uint)System.Diagnostics.Process.GetCurrentProcess().Id % 100000;
#endif
                }
                return s_Profile;
            }
        }

        public static string ShortName => IsMainEditor ? "Main" : AuthProfile;

        public static string CommandLineValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        static string Sanitize(string s)
        {
            var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(30).ToArray();
            return chars.Length == 0 ? "p" : new string(chars);
        }
    }
}
