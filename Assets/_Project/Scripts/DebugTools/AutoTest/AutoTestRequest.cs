using System;
using System.IO;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Automated MPPM test request. Written to persistentDataPath/autotest_request.json (shared by the main
    /// editor and its clone). Only honoured when fresh, so normal play is never affected.
    /// </summary>
    [Serializable]
    public class AutoTestRequest
    {
        public string suite = "stage1";
        public long createdUnix;
        public string reportDir = "";
        public bool quitWhenDone = true;

        public static string RequestPath => Path.Combine(Application.persistentDataPath, "autotest_request.json");

        static AutoTestRequest s_Cached;
        static bool s_Loaded;

        public static AutoTestRequest Current
        {
            get
            {
                if (s_Loaded) return s_Cached;
                s_Loaded = true;
                try
                {
                    if (!File.Exists(RequestPath)) return null;
                    var r = JsonUtility.FromJson<AutoTestRequest>(File.ReadAllText(RequestPath));
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (r == null || now - r.createdUnix > 600) return null;
                    s_Cached = r;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[NO:AutoTest] bad request: " + e.Message);
                }
                return s_Cached;
            }
        }

        public static void Consume()
        {
            try
            {
                if (File.Exists(RequestPath)) File.Delete(RequestPath);
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}
