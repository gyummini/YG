using UnityEngine;
using UnityEngine.UIElements;

namespace NightOffice
{
    /// <summary>
    /// 결과 화면: shown to both players when the night ends — 04:00 (퇴근) or the field vanished. Lists tonight's
    /// complaints and how many were handled; the host starts the next night or goes back to the lobby.
    /// </summary>
    public class ResultsScreen : MonoBehaviour, IResultsScreenTest
    {
        const string Key = "results";

        VisualElement m_Screen, m_List;
        Label m_Title, m_Sub, m_Count, m_Wait;
        Button m_Next, m_Lobby;
        bool m_Bound, m_Shown;

        public static ResultsScreen I { get; private set; }
        public bool Shown => m_Shown;
        public string TitleText => m_Title != null ? m_Title.text : null;
        public string CountText => m_Count != null ? m_Count.text : null;

        void Awake()
        {
            I = this;
            OfficeScreens.Results = this;
        }

        void Bind(VisualElement root)
        {
            m_Screen = root.Q("resultsScreen");
            if (m_Screen == null) return;
            m_Bound = true;
            m_Title = m_Screen.Q<Label>("resultTitle");
            m_Sub = m_Screen.Q<Label>("resultSub");
            m_Count = m_Screen.Q<Label>("resultCount");
            m_List = m_Screen.Q("resultList");
            m_Wait = m_Screen.Q<Label>("resultWait");
            m_Next = m_Screen.Q<Button>("resultNext");
            m_Lobby = m_Screen.Q<Button>("resultLobby");
            m_Next.clicked += () => NightDirector.I?.RequestStartNightRpc();
            m_Lobby.clicked += () => NightDirector.I?.RequestBackToLobbyRpc();
        }

        void Update()
        {
            if (!m_Bound)
            {
                if (UIRoot.I != null && UIRoot.I.Root != null) Bind(UIRoot.I.Root);
                if (!m_Bound) return;
            }
            var night = NightDirector.I;
            bool show = night != null && night.IsSpawned && night.Phase.Value == NightPhase.Ended;
            if (show == m_Shown) return;
            m_Shown = show;
            UIRoot.Show(m_Screen, show);
            if (show)
            {
                OfficeScreensController.I?.Close();
                Fill(night);
                UIState.Push(Key);
            }
            else UIState.Pop(Key);
        }

        void Fill(NightDirector night)
        {
            bool vanished = night.Outcome.Value == NightOutcome.FieldVanished;
            var clock = GameClock.I;
            string at = clock != null ? GameClock.Format(clock.Minutes) : "--:--";
            m_Title.text = vanished ? "현장 실종" : "퇴근";
            m_Title.EnableInClassList("vanished", vanished);
            m_Sub.text = vanished
                ? $"{at} · 현장이 돌아오지 않았다. 그 밤은 여기서 끝난다. 다음 밤은 다시 둘이 시작한다."
                : $"{at} · 오늘 밤 근무 끝.";
            int target = GameSettings.I.night.targetComplaints;
            m_Count.text = $"처리한 민원 {night.HandledComplaints.Value} / {target}";

            m_List.Clear();
            var board = ComplaintBoard.I;
            if (board != null && board.IsSpawned)
                foreach (var c in board.Items)
                {
                    var row = new VisualElement();
                    row.AddToClassList("result-row");
                    var time = new Label(GameClock.Format(c.Minute));
                    time.AddToClassList("result-time");
                    var unit = new Label($"{c.Unit}호");
                    unit.AddToClassList("result-unit");
                    var text = new Label(ComplaintBoard.TextOf(c));
                    text.AddToClassList("result-text");
                    bool done = c.State == ComplaintState.Handled;
                    var state = new Label(done ? $"처리 {GameClock.Format(c.HandledMinute)}" : "미처리");
                    state.AddToClassList("result-state");
                    state.EnableInClassList("done", done);
                    row.Add(time);
                    row.Add(unit);
                    row.Add(text);
                    row.Add(state);
                    m_List.Add(row);
                }

            bool host = night.IsServer;
            UIRoot.Show(m_Next, host);
            UIRoot.Show(m_Lobby, host);
            UIRoot.Show(m_Wait, !host);
        }
    }
}
