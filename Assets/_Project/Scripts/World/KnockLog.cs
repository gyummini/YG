using System;
using System.Collections.Generic;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Server-side record of knocks on the office door. Knocks separated by less than a pause belong to one
    /// pattern; the mimic replays the last pattern the field player knocked.
    /// </summary>
    public static class KnockLog
    {
        public const float GroupPause = 0.6f;   // gap that starts a new group (e.g. 2-1)
        public const float PatternEnd = 1.8f;   // silence that ends a pattern

        public struct Pattern
        {
            public float[] Offsets; // seconds from the first knock
            public bool ByMimic;
            public double EndedAt;
            public string Code => ToCode(Offsets);
        }

        static readonly List<float> s_Current = new List<float>();
        static double s_CurrentStart;
        static double s_LastKnock = -100;
        static bool s_CurrentByMimic;

        public static readonly List<Pattern> Patterns = new List<Pattern>();
        public static event Action<Pattern> PatternCompleted;

        public static Pattern? LastFieldPattern
        {
            get
            {
                for (int i = Patterns.Count - 1; i >= 0; i--)
                    if (!Patterns[i].ByMimic)
                        return Patterns[i];
                return null;
            }
        }

        public static void ServerRecord(Door door, bool byMimic)
        {
            double now = Time.timeAsDouble;
            if (s_Current.Count > 0 && now - s_LastKnock > PatternEnd) Flush();
            if (s_Current.Count == 0)
            {
                s_CurrentStart = now;
                s_CurrentByMimic = byMimic;
            }
            s_Current.Add((float)(now - s_CurrentStart));
            s_LastKnock = now;
        }

        /// <summary>Call regularly (server) so a finished pattern is closed even without another knock.</summary>
        public static void Tick()
        {
            if (s_Current.Count > 0 && Time.timeAsDouble - s_LastKnock > PatternEnd) Flush();
        }

        static void Flush()
        {
            var p = new Pattern { Offsets = s_Current.ToArray(), ByMimic = s_CurrentByMimic, EndedAt = s_LastKnock };
            s_Current.Clear();
            Patterns.Add(p);
            GameLog.Info("Knock", $"{(p.ByMimic ? "흉내쟁이" : "현장")} 노크 {p.Code}");
            PatternCompleted?.Invoke(p);
        }

        public static void Clear()
        {
            s_Current.Clear();
            Patterns.Clear();
            s_LastKnock = -100;
        }

        /// <summary>"2-1-3" style code from knock offsets.</summary>
        public static string ToCode(float[] offsets)
        {
            if (offsets == null || offsets.Length == 0) return "";
            var groups = new List<int> { 1 };
            for (int i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] - offsets[i - 1] >= GroupPause) groups.Add(1);
                else groups[groups.Count - 1]++;
            }
            return string.Join("-", groups);
        }
    }
}
