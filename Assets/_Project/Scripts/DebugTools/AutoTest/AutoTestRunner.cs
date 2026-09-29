using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// Runs scripted 2-player checks on the host (main editor) under Multiplayer Play Mode and writes a JSON
    /// report. The client (MPPM clone) is driven through <see cref="AutoTestNet"/>.
    /// </summary>
    public partial class AutoTestRunner : MonoBehaviour
    {
        [Serializable]
        public class CheckResult
        {
            public string name;
            public bool pass;
            public string detail;
        }

        [Serializable]
        public class Report
        {
            public string suite;
            public string started;
            public string finished;
            public int passed;
            public int failed;
            public List<CheckResult> checks = new List<CheckResult>();
            public List<string> notes = new List<string>();
        }

        Report m_Report;
        AutoTestRequest m_Request;

        void Start()
        {
            m_Request = AutoTestRequest.Current;
            if (m_Request == null || !InstanceInfo.IsMainEditor)
            {
                enabled = false;
                return;
            }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            m_Report = new Report { suite = m_Request.suite, started = DateTime.Now.ToString("s") };
            yield return WaitFor("2명 접속 + 역할 배정", () => PlayerNet.All.Count >= 2 && PlayerNet.Field != null && PlayerNet.Control != null, 300f);
            if (PlayerNet.All.Count >= 2)
            {
                yield return new WaitForSeconds(2f);
                IEnumerator suite = null;
                switch (m_Request.suite)
                {
                    case "stage1": suite = Stage1(); break;
                    case "stage2": suite = Stage2(); break;
                    case "stage3": suite = Stage3(); break;
                    case "online": suite = Online(); break;
                    case "map": suite = MapSuite(); break;
                }
                if (suite != null) yield return StartCoroutine(Guard(suite));
                else Note("unknown suite " + m_Request.suite);
            }
            Finish();
        }

        /// <summary>Runs a suite, turning an exception into a failed check instead of a silent stop.</summary>
        IEnumerator Guard(IEnumerator inner)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    cur = inner.Current;
                }
                catch (Exception e)
                {
                    Check("예외 없이 완료", false, e.ToString());
                    yield break;
                }
                yield return cur;
            }
        }

        void Finish()
        {
            m_Report.finished = DateTime.Now.ToString("s");
            foreach (var c in m_Report.checks)
                if (c.pass) m_Report.passed++;
                else m_Report.failed++;
            string dir = string.IsNullOrEmpty(m_Request.reportDir) ? Application.persistentDataPath : m_Request.reportDir;
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"autotest_{m_Request.suite}.json");
                File.WriteAllText(path, JsonUtility.ToJson(m_Report, true));
                GameLog.Info("AutoTest", $"report written {path} pass={m_Report.passed} fail={m_Report.failed}");
            }
            catch (Exception e)
            {
                GameLog.Error("AutoTest", "report write failed " + e.Message);
            }
            AutoTestRequest.Consume();
#if UNITY_EDITOR
            if (m_Request.quitWhenDone)
            {
                // Stop the whole Multiplayer Play Mode scenario (not just play mode) so it releases its
                // assembly-reload lock and the clone leaves play mode too.
                try
                {
                    if (Unity.PlayMode.Editor.PlayModeScenarioManager.State != Unity.PlayMode.Editor.PlayModeScenarioState.Idle)
                        Unity.PlayMode.Editor.PlayModeScenarioManager.Stop();
                    else UnityEditor.EditorApplication.isPlaying = false;
                }
                catch (Exception)
                {
                    UnityEditor.EditorApplication.isPlaying = false;
                }
            }
#endif
        }

        // ------------------------------------------------------------------ helpers
        void Check(string name, bool pass, string detail = "")
        {
            m_Report.checks.Add(new CheckResult { name = name, pass = pass, detail = detail });
            GameLog.Info("AutoTest", $"{(pass ? "PASS" : "FAIL")} {name} {detail}");
        }

        void Note(string s)
        {
            m_Report.notes.Add(s);
            GameLog.Info("AutoTest", "note: " + s);
        }

        IEnumerator WaitFor(string name, Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (!cond() && Time.time - t0 < timeout) yield return null;
            Check(name, cond(), $"{Time.time - t0:0.00}s");
        }

        Observation m_ClientObs;
        bool m_ClientObsOk;

        IEnumerator AskClient()
        {
            m_ClientObsOk = false;
            int id = AutoTestNet.I.AskClient();
            float t0 = Time.time;
            while (Time.time - t0 < 4f)
            {
                if (AutoTestNet.I.TryGetReport(id, out m_ClientObs))
                {
                    m_ClientObsOk = true;
                    yield break;
                }
                yield return null;
            }
            Note("client report timeout");
        }

        static Observation Host => Observation.Capture();

        static PlayerNet FieldP => PlayerNet.Field;
        static PlayerNet ControlP => PlayerNet.Control;

        /// <summary>Scripted input for the field player (the client in the default role assignment).</summary>
        static void FieldInput(bool ptt = false, bool talk = false, Vector2 move = default, bool headDown = false, bool eyes = false)
        {
            var f = FieldP;
            if (f == null) return;
            if (f.IsOwner) AutoTestNet.ApplyLocalInput(true, move, ptt, headDown, eyes, talk);
            else AutoTestNet.I.ClientInputRpc(true, move, ptt, headDown, eyes, talk);
        }

        static void ControlInput(bool ptt = false, bool talk = false, Vector2 move = default)
        {
            var c = ControlP;
            if (c == null) return;
            if (c.IsOwner) AutoTestNet.ApplyLocalInput(true, move, ptt, false, false, talk);
            else AutoTestNet.I.ClientInputRpc(true, move, ptt, false, false, talk);
        }

        static void ReleaseInputs()
        {
            AutoTestNet.ApplyLocalInput(false, Vector2.zero, false, false, false, false);
            AutoTestNet.I.ClientInputRpc(false, Vector2.zero, false, false, false, false);
        }

        static Door FindDoor(DoorKind kind, int floor)
        {
            foreach (var d in Door.All)
                if (d != null && d.kind == kind && d.floor == floor)
                    return d;
            return null;
        }

        static string Route(PlayerNet listenerSide)
        {
            // route of "the other player" as heard on the host
            var local = PlayerNet.Local;
            var other = local != null ? local.Other : null;
            return other != null && other.remoteVoice != null ? other.remoteVoice.Route.Mode.ToString() : "-";
        }

        static float HostHearsLevel()
        {
            var local = PlayerNet.Local;
            var other = local != null ? local.Other : null;
            return other != null && other.remoteVoice != null ? other.remoteVoice.Level : 0f;
        }

        float m_Heard;

        /// <summary>
        /// Max output level of the other player's voice (host side) over a window. The synthetic voice starts at a
        /// random point of a babble clip with pauses, so a single short peak window can land in silence.
        /// </summary>
        IEnumerator ListenHost(float seconds)
        {
            m_Heard = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                m_Heard = Mathf.Max(m_Heard, HostHearsLevel());
                yield return null;
            }
        }

        /// <summary>Client side of <see cref="ListenHost"/>: best of a few client reports (each holds a 1 s peak).</summary>
        IEnumerator ListenClient(int reports)
        {
            m_Heard = 0f;
            for (int i = 0; i < reports; i++)
            {
                yield return AskClient();
                if (m_ClientObsOk) m_Heard = Mathf.Max(m_Heard, m_ClientObs.otherPeakLevel);
                yield return new WaitForSeconds(0.8f);
            }
        }

        /// <summary>Peak output level of the other player's voice over the last second (host side).</summary>
        static float HostHearsPeak()
        {
            var local = PlayerNet.Local;
            var other = local != null ? local.Other : null;
            return other != null && other.remoteVoice != null ? other.remoteVoice.PeakLevel : 0f;
        }
    }
}
