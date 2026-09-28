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

        void Update()
        {
            var cm = ConnectionManager.I;
            if (cm == null || Time.time < m_NextTry) return;
            m_NextTry = Time.time + 2f;
            if (InstanceInfo.IsMainEditor)
            {
                if (!m_Hosted && !cm.IsOnline)
                {
                    m_Hosted = true;
                    cm.HostLan();
                }
            }
            else if (!cm.IsOnline)
            {
                cm.JoinLan("127.0.0.1");
            }
        }
    }
}
