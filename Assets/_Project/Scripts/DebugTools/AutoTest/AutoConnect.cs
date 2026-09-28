using UnityEngine;

namespace NightOffice
{
    /// <summary>Under an automated test request: main editor hosts on LAN, the MPPM clone joins.</summary>
    public class AutoConnect : MonoBehaviour
    {
        float m_NextTry;
        bool m_Hosted;

        void Start()
        {
            var req = AutoTestRequest.Current;
            if (req == null)
            {
                enabled = false;
                return;
            }
            GameLog.OpenFile(System.IO.Path.Combine(Application.persistentDataPath, $"autotest_{(InstanceInfo.IsMainEditor ? "host" : "client")}.log"));
            GameLog.Info("AutoTest", $"request suite={req.suite} main={InstanceInfo.IsMainEditor}");
            m_NextTry = Time.time + (InstanceInfo.IsMainEditor ? 0.5f : 3f);
        }

        bool m_CodeWritten;

        void Update()
        {
            var cm = ConnectionManager.I;
            var req = AutoTestRequest.Current;
            if (cm == null || req == null || Time.time < m_NextTry) return;
            m_NextTry = Time.time + 2f;
            bool relay = req.net == "relay";
            if (InstanceInfo.IsMainEditor)
            {
                if (!m_Hosted && !cm.IsOnline && !cm.Busy)
                {
                    m_Hosted = true;
                    if (relay)
                    {
                        try { System.IO.File.Delete(req.JoinCodePath); }
                        catch (System.Exception) { }
                        cm.HostRelay();
                    }
                    else cm.HostLan();
                }
                if (relay && !m_CodeWritten && cm.UsingRelay && !string.IsNullOrEmpty(cm.JoinCode))
                {
                    System.IO.File.WriteAllText(req.JoinCodePath, cm.JoinCode);
                    m_CodeWritten = true;
                    GameLog.Info("AutoTest", "join code written " + cm.JoinCode);
                }
            }
            else if (!cm.IsOnline && !cm.Busy)
            {
                if (!relay)
                {
                    cm.JoinLan("127.0.0.1");
                }
                else if (System.IO.File.Exists(req.JoinCodePath) &&
                         System.DateTime.Now - System.IO.File.GetLastWriteTime(req.JoinCodePath) < System.TimeSpan.FromMinutes(5))
                {
                    cm.JoinRelay(System.IO.File.ReadAllText(req.JoinCodePath).Trim());
                }
            }
        }
    }
}
