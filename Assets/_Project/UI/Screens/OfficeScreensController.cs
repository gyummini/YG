using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    /// <summary>
    /// 단말기 (매뉴얼 · 계기판 · 원격 조작 · 도면) and the shift fax. Opened from the office devices; while open the
    /// player cannot move or look around (push-to-talk still works). Esc closes.
    /// </summary>
    public class OfficeScreensController : MonoBehaviour, IOfficeScreensTest
    {
        const string TermKey = "terminal";
        const string FaxKey = "fax";

        enum Page
        {
            Manual,
            Gauges,
            Remote,
            Plan,
        }

        bool m_Bound;
        VisualElement m_Term, m_Fax, m_PowerOut;
        Label m_Clock, m_Status;
        VisualElement m_Registered;
        readonly Dictionary<Page, VisualElement> m_Pages = new Dictionary<Page, VisualElement>();
        readonly Dictionary<Page, Button> m_Tabs = new Dictionary<Page, Button>();
        Page m_Page = Page.Manual;

        // manual
        VisualElement m_TagGroups, m_CandidateList, m_CardBody, m_CardBranches, m_CardCommon, m_CommonPicto, m_CardPicto;
        Label m_CandidateCaption, m_CardEmpty, m_CardName, m_CardCategory, m_CardNature, m_CardCondition, m_CardWarning, m_CommonText;
        ForkLines m_Fork;
        readonly Dictionary<ClueAttr, string> m_Selected = new Dictionary<ClueAttr, string>();
        readonly List<(ClueAttr attr, string value, Button button)> m_Chips = new List<(ClueAttr, string, Button)>();
        string m_CardPictoClass, m_CommonPictoClass;

        // gauges
        Label m_ElevFloor, m_ElevDir, m_ElevCount, m_ElevDoors;
        readonly PowerSparkline[] m_Spark = new PowerSparkline[L.MaxFloor + 1];
        readonly Label[] m_Kw = new Label[L.MaxFloor + 1];
        ScrollView m_CardLog;
        int m_CardLogCount = -1;
        VisualElement m_RegistryRows;
        uint m_RegistryStamp = uint.MaxValue;
        float m_NextSample;

        // remote
        readonly List<(int floor, int section, Button button)> m_LightButtons = new List<(int, int, Button)>();
        readonly List<(string key, Button button, string label)> m_DoorButtons = new List<(string, Button, string)>();
        readonly List<Button> m_RemoteButtons = new List<Button>();
        Label m_ElevRemoteState;

        // plan
        FloorPlanView m_Plan;
        Label m_PlanNotes;
        readonly List<(int floor, Button button)> m_PlanFloorButtons = new List<(int, Button)>();

        // fax
        Label m_FaxMeta, m_FaxCallSigns;
        VisualElement m_FaxRules, m_FaxCodes;
        int m_FaxShownPrint = -1;

        float m_NextRefresh;

        public bool TerminalOpen => m_Term != null && !m_Term.ClassListContains("hidden");
        public bool FaxOpen => m_Fax != null && !m_Fax.ClassListContains("hidden");
        public static OfficeScreensController I { get; private set; }

        void Awake()
        {
            I = this;
            OfficeScreens.Test = this;
        }

        void OnEnable() => OfficeScreens.OpenRequested += Open;

        void OnDisable() => OfficeScreens.OpenRequested -= Open;

        // ================================================================ binding
        void Bind(VisualElement root)
        {
            m_Term = root.Q("terminalScreen");
            m_Fax = root.Q("faxScreen");
            if (m_Term == null || m_Fax == null) return;
            m_Bound = true;
            m_PowerOut = m_Term.Q("powerOut");
            m_Clock = m_Term.Q<Label>("termClock");
            m_Status = m_Term.Q<Label>("termStatus");
            m_Registered = m_Term.Q("termRegistered");
            m_Term.Q<Button>("termClose").clicked += Close;

            BindTab(Page.Manual, "tabManual", "pageManual");
            BindTab(Page.Gauges, "tabGauges", "pageGauges");
            BindTab(Page.Remote, "tabRemote", "pageRemote");
            BindTab(Page.Plan, "tabPlan", "pagePlan");

            BindManual();
            BindGauges();
            BindRemote();
            BindPlan();
            BindFax();
            ShowPage(Page.Manual);
        }

        void BindTab(Page page, string tabName, string pageName)
        {
            var tab = m_Term.Q<Button>(tabName);
            var p = m_Term.Q(pageName);
            m_Tabs[page] = tab;
            m_Pages[page] = p;
            tab.clicked += () => ShowPage(page);
        }

        void ShowPage(Page page)
        {
            m_Page = page;
            foreach (var kv in m_Pages) UIRoot.Show(kv.Value, kv.Key == page);
            foreach (var kv in m_Tabs) kv.Value.EnableInClassList("selected", kv.Key == page);
            AudioService.I?.PlayUi(SfxId.TerminalClick);
            Refresh();
        }

        // ================================================================ 매뉴얼
        void BindManual()
        {
            m_TagGroups = m_Term.Q("tagGroups");
            m_CandidateList = m_Term.Q("candidateList");
            m_CandidateCaption = m_Term.Q<Label>("candidateCaption");
            m_CardEmpty = m_Term.Q<Label>("cardEmpty");
            m_CardBody = m_Term.Q("cardBody");
            m_CardPicto = m_Term.Q("cardPicto");
            m_CardName = m_Term.Q<Label>("cardName");
            m_CardCategory = m_Term.Q<Label>("cardCategory");
            m_CardNature = m_Term.Q<Label>("cardNature");
            m_CardCommon = m_Term.Q("cardCommon");
            m_CommonPicto = m_Term.Q("commonPicto");
            m_CommonText = m_Term.Q<Label>("commonText");
            m_CardCondition = m_Term.Q<Label>("cardCondition");
            m_Fork = m_Term.Q<ForkLines>("cardFork");
            m_CardBranches = m_Term.Q("cardBranches");
            m_CardWarning = m_Term.Q<Label>("cardWarning");
            m_Term.Q<Button>("tagReset").clicked += () =>
            {
                m_Selected.Clear();
                RefreshManual();
            };

            var catalog = EntityCatalog.I;
            if (catalog == null) return;
            string section = null;
            foreach (var group in catalog.TagGroups())
            {
                var attr = group.Key;
                string sec = attr == ClueAttr.FirstImpression ? "첫인상 (등록된 묶음)" : ClueAttrText.FieldSide(attr) ? "현장이 확인하는 것" : "상황실이 확인하는 것";
                if (sec != section)
                {
                    section = sec;
                    var s = new Label(sec);
                    s.AddToClassList("tag-section");
                    m_TagGroups.Add(s);
                }
                var box = new VisualElement();
                box.AddToClassList("tag-group");
                var cap = new Label(ClueAttrText.Label(attr));
                cap.AddToClassList("tag-group-caption");
                box.Add(cap);
                var row = new VisualElement();
                row.AddToClassList("tag-row");
                foreach (var value in group.Value)
                {
                    var b = new Button { text = value };
                    b.AddToClassList("tag-chip");
                    var a = attr;
                    var v = value;
                    b.clicked += () =>
                    {
                        if (m_Selected.TryGetValue(a, out var cur) && cur == v) m_Selected.Remove(a);
                        else m_Selected[a] = v;
                        AudioService.I?.PlayUi(SfxId.TerminalClick);
                        RefreshManual();
                    };
                    m_Chips.Add((attr, value, b));
                    row.Add(b);
                }
                box.Add(row);
                m_TagGroups.Add(box);
            }
            RefreshManual();
        }

        static VisualElement Picto(string key, string sizeClass)
        {
            var e = new VisualElement();
            e.AddToClassList("picto");
            e.AddToClassList(sizeClass);
            if (!string.IsNullOrEmpty(key)) e.AddToClassList("picto--" + key);
            return e;
        }

        static void SetPicto(VisualElement e, ref string current, string key)
        {
            if (current != null) e.RemoveFromClassList(current);
            current = string.IsNullOrEmpty(key) ? null : "picto--" + key;
            if (current != null) e.AddToClassList(current);
        }

        void RefreshManual()
        {
            foreach (var (attr, value, button) in m_Chips)
                button.EnableInClassList("selected", m_Selected.TryGetValue(attr, out var v) && v == value);
            var catalog = EntityCatalog.I;
            if (catalog == null) return;
            var found = catalog.Filter(m_Selected);
            m_CandidateCaption.text = $"후보 {found.Count}";
            m_CandidateList.Clear();
            foreach (var e in found)
            {
                var row = new VisualElement();
                row.AddToClassList("candidate");
                row.Add(Picto(e.pictogram, "candidate-picto"));
                var texts = new VisualElement();
                texts.AddToClassList("candidate-texts");
                var name = new Label($"{e.displayName} · {e.CategoryLabel}");
                name.AddToClassList("candidate-name");
                var nature = new Label(e.nature);
                nature.AddToClassList("candidate-nature");
                texts.Add(name);
                texts.Add(nature);
                row.Add(texts);
                m_CandidateList.Add(row);
            }

            bool one = found.Count == 1;
            UIRoot.Show(m_CardEmpty, !one);
            UIRoot.Show(m_CardBody, one);
            m_CardEmpty.text = found.Count == 0 ? "맞는 후보가 없습니다. 태그를 다시 확인하세요." : "태그로 후보를 하나까지 좁히면 대응이 나옵니다.";
            if (one) ShowCard(found[0]);
        }

        void ShowCard(EntityDefinition e)
        {
            SetPicto(m_CardPicto, ref m_CardPictoClass, e.pictogram);
            m_CardName.text = e.displayName;
            m_CardCategory.text = $"{e.CategoryLabel} · 묶음 {e.bundle}";
            m_CardNature.text = e.nature;

            bool common = e.common != null && !string.IsNullOrEmpty(e.common.text);
            UIRoot.Show(m_CardCommon, common);
            if (common)
            {
                m_CommonText.text = "먼저: " + e.common.text;
                SetPicto(m_CommonPicto, ref m_CommonPictoClass, e.common.pictograms != null && e.common.pictograms.Length > 0 ? e.common.pictograms[0] : null);
            }

            bool hasCondition = !string.IsNullOrEmpty(e.condition);
            m_CardCondition.text = hasCondition ? $"조건: {e.condition}" : "조건 없음";
            m_Fork.Branches = e.branches.Length;
            m_CardBranches.Clear();
            foreach (var br in e.branches)
            {
                var col = new VisualElement();
                col.AddToClassList("branch");
                if (!string.IsNullOrEmpty(br.when))
                {
                    var when = new Label(br.when);
                    when.AddToClassList("branch-when");
                    col.Add(when);
                }
                var pictos = new VisualElement();
                pictos.AddToClassList("branch-pictos");
                if (br.pictograms != null)
                    foreach (var key in br.pictograms)
                        pictos.Add(Picto(key, "branch-picto"));
                col.Add(pictos);
                var text = new Label(br.text);
                text.AddToClassList("branch-text");
                col.Add(text);
                m_CardBranches.Add(col);
            }
            m_CardWarning.text = e.hasWarning ? $"경고 행동 (첫 실수에만): {e.warning}" : string.IsNullOrEmpty(e.warning) ? "" : e.warning;
        }

        // ================================================================ 계기판
        void BindGauges()
        {
            m_ElevFloor = m_Term.Q<Label>("elevFloor");
            m_ElevDir = m_Term.Q<Label>("elevDir");
            m_ElevCount = m_Term.Q<Label>("elevCount");
            m_ElevDoors = m_Term.Q<Label>("elevDoors");
            m_CardLog = m_Term.Q<ScrollView>("cardLogList");
            m_RegistryRows = m_Term.Q("registryRows");
            var rows = m_Term.Q("powerRows");
            for (int f = L.MaxFloor; f >= 1; f--)
            {
                var row = new VisualElement();
                row.AddToClassList("power-row");
                var lab = new Label(f + "F");
                lab.AddToClassList("power-floor");
                var spark = new PowerSparkline();
                spark.AddToClassList("power-spark");
                var kw = new Label("-");
                kw.AddToClassList("power-kw");
                row.Add(lab);
                row.Add(spark);
                row.Add(kw);
                rows.Add(row);
                m_Spark[f] = spark;
                m_Kw[f] = kw;
            }
        }

        void SamplePower()
        {
            var net = LightingNet.I;
            if (net == null || !net.IsSpawned || Time.unscaledTime < m_NextSample) return;
            m_NextSample = Time.unscaledTime + 0.25f;
            var r = net.Power.Value;
            for (int f = 1; f <= L.MaxFloor; f++) m_Spark[f]?.Push(r[f]);
        }

        void RefreshGauges()
        {
            var el = Elevator.I;
            if (el != null && el.IsSpawned)
            {
                var st = el.State.Value;
                m_ElevFloor.text = el.NearestFloor.ToString();
                m_ElevDir.text = st.Halted ? "■" : st.Dir > 0 ? "▲" : st.Dir < 0 ? "▼" : "·";
                m_ElevCount.text = $"탑승 {st.Occupancy}명";
                m_ElevDoors.text = st.Halted ? "정지됨" : el.DoorState switch
                {
                    ElevatorDoorState.Open => "문 열림",
                    ElevatorDoorState.Opening => "문 여는 중",
                    ElevatorDoorState.Closing => "문 닫는 중",
                    _ => "문 닫힘",
                };
            }
            var net = LightingNet.I;
            if (net != null && net.IsSpawned)
            {
                var r = net.Power.Value;
                for (int f = 1; f <= L.MaxFloor; f++) m_Kw[f].text = $"{r[f]:0.00} kW";
            }

            var log = CardLog.I;
            if (log != null && log.IsSpawned && log.Records.Count != m_CardLogCount)
            {
                m_CardLogCount = log.Records.Count;
                m_CardLog.Clear();
                for (int i = log.Records.Count - 1; i >= 0 && i >= log.Records.Count - 60; i--)
                {
                    var rec = log.Records[i];
                    var row = new VisualElement();
                    row.AddToClassList("cardlog-row");
                    row.EnableInClassList("denied", !rec.Ok);
                    var t = new Label(GameClock.Format(rec.Minute));
                    t.AddToClassList("cardlog-time");
                    var l = new Label(rec.Label.ToString());
                    l.AddToClassList("cardlog-label");
                    var ok = new Label(rec.Ok ? "승인" : "거부");
                    ok.AddToClassList("cardlog-ok");
                    row.Add(t);
                    row.Add(l);
                    row.Add(ok);
                    m_CardLog.Add(row);
                }
                if (m_CardLogCount == 0) m_CardLog.Add(new Label("(기록 없음)"));
            }

            var reg = UnitRegistry.I;
            if (reg != null && reg.IsSpawned)
            {
                uint stamp = reg.VacantMask.Value * 31u + reg.StorageMask.Value;
                if (stamp != m_RegistryStamp)
                {
                    m_RegistryStamp = stamp;
                    m_RegistryRows.Clear();
                    for (int f = L.MaxFloor; f >= 2; f--)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("registry-row");
                        var lab = new Label(f + "F");
                        lab.AddToClassList("registry-floor");
                        row.Add(lab);
                        foreach (var u in L.UnitsOnFloor(f))
                        {
                            var s = reg.StatusOf(u.Number);
                            var chip = new Label(u.Number.ToString());
                            chip.AddToClassList("unit-chip");
                            chip.EnableInClassList("vacant", s == UnitStatus.Vacant);
                            chip.EnableInClassList("storage", s == UnitStatus.Storage);
                            row.Add(chip);
                        }
                        m_RegistryRows.Add(row);
                    }
                }
            }
        }

        // ================================================================ 원격 조작
        void BindRemote()
        {
            var lightRows = m_Term.Q("lightRows");
            for (int f = L.MaxFloor; f >= 1; f--)
            {
                var row = new VisualElement();
                row.AddToClassList("remote-row");
                var lab = new Label(f + "F");
                lab.AddToClassList("remote-floor");
                row.Add(lab);
                int sections = f == 1 ? 1 : 2;
                for (int s = 0; s < sections; s++)
                {
                    int floor = f, section = s;
                    var b = new Button();
                    b.AddToClassList("switch-btn");
                    b.clicked += () =>
                    {
                        var net = LightingNet.I;
                        if (net == null) return;
                        net.RemoteSetSectionRpc(floor, section, !net.SectionOn(floor, section));
                        AudioService.I?.PlayUi(SfxId.TerminalClick);
                    };
                    m_LightButtons.Add((f, s, b));
                    m_RemoteButtons.Add(b);
                    row.Add(b);
                }
                lightRows.Add(row);
            }

            var calls = m_Term.Q("elevCalls");
            for (int f = 1; f <= L.MaxFloor; f++)
            {
                int floor = f;
                var b = new Button { text = f + "F 호출" };
                b.AddToClassList("elev-call");
                b.clicked += () =>
                {
                    Elevator.I?.RemoteCallRpc(floor);
                    AudioService.I?.PlayUi(SfxId.TerminalClick);
                };
                m_RemoteButtons.Add(b);
                calls.Add(b);
            }
            var stop = m_Term.Q<Button>("elevStop");
            stop.clicked += () => Elevator.I?.RemoteStopRpc();
            var close = m_Term.Q<Button>("elevClose");
            close.clicked += () => Elevator.I?.RemoteCloseRpc();
            m_RemoteButtons.Add(stop);
            m_RemoteButtons.Add(close);
            m_ElevRemoteState = m_Term.Q<Label>("elevRemoteState");

            var doorRows = m_Term.Q("doorRows");
            for (int f = L.MaxFloor; f >= 1; f--)
            {
                var row = new VisualElement();
                row.AddToClassList("remote-row");
                var lab = new Label(f + "F");
                lab.AddToClassList("remote-floor");
                row.Add(lab);
                AddDoorButton(row, "fireW" + f, "서쪽 계단");
                if (f >= 2) AddDoorButton(row, "fireMid" + f, "복도");
                AddDoorButton(row, "fireE" + f, "동쪽 계단");
                doorRows.Add(row);
            }
        }

        void AddDoorButton(VisualElement row, string key, string label)
        {
            var b = new Button();
            b.AddToClassList("switch-btn");
            b.clicked += () =>
            {
                var d = Door.ByKey(key);
                if (d == null || BuildingControl.I == null) return;
                BuildingControl.I.RemoteLockRpc(new FixedString32Bytes(key), !d.Locked.Value);
                AudioService.I?.PlayUi(SfxId.TerminalClick);
            };
            m_DoorButtons.Add((key, b, label));
            m_RemoteButtons.Add(b);
            row.Add(b);
        }

        void RefreshRemote(bool powerOut)
        {
            var net = LightingNet.I;
            foreach (var (floor, section, b) in m_LightButtons)
            {
                bool on = net != null && net.SectionOn(floor, section);
                string where = floor == 1 ? "1층 전체" : L.SectionLabel(section) + " 구간";
                b.text = $"{where}  {(on ? "켜짐" : "꺼짐")}";
                b.EnableInClassList("on", on);
            }
            foreach (var (key, b, label) in m_DoorButtons)
            {
                var d = Door.ByKey(key);
                bool locked = d != null && d.Locked.Value;
                b.text = $"{label}  {(locked ? "잠김" : "풀림")}";
                b.EnableInClassList("locked", locked);
            }
            foreach (var b in m_RemoteButtons) b.SetEnabled(!powerOut);
            var el = Elevator.I;
            if (el != null && el.IsSpawned)
            {
                var st = el.State.Value;
                m_ElevRemoteState.text = $"현재 {el.NearestFloor}F · {(st.Halted ? "정지됨" : st.Dir == 0 ? "대기" : st.Dir > 0 ? "올라가는 중" : "내려가는 중")} · 탑승 {st.Occupancy}명";
            }
        }

        // ================================================================ 도면
        void BindPlan()
        {
            m_Plan = m_Term.Q<FloorPlanView>("floorPlan");
            m_PlanNotes = m_Term.Q<Label>("planNotes");
            var floors = m_Term.Q("planFloors");
            for (int f = L.MaxFloor; f >= 1; f--)
            {
                int floor = f;
                var b = new Button { text = f + "F" };
                b.AddToClassList("plan-floor-btn");
                b.clicked += () =>
                {
                    m_Plan.Floor = floor;
                    AudioService.I?.PlayUi(SfxId.TerminalClick);
                    RefreshPlan();
                };
                m_PlanFloorButtons.Add((f, b));
                floors.Add(b);
            }
            m_Plan.Floor = 3;
        }

        void RefreshPlan()
        {
            foreach (var (floor, b) in m_PlanFloorButtons) b.EnableInClassList("selected", floor == m_Plan.Floor);
            m_Plan.MarkDirtyRepaint();
            int f = m_Plan.Floor;
            if (f >= 2)
            {
                var panel = L.Recess(L.PanelRecess(f)).Label;
                m_PlanNotes.text =
                    "2F~4F는 모두 같은 구조.\n" +
                    "서쪽 계단에서 걸으면 세대 문은 항상 오른쪽, 난간은 왼쪽.\n\n" +
                    $"{f}F 배전함: '{panel}' 안.\n\n" +
                    "초록 = 공실, 노랑 = 창고 (오늘 밤 명부).\n" +
                    "밝은 복도 = 내가 켜 둔 조명 구간.\n" +
                    "빨간 선 = 방화문 (잠그면 '잠김').";
            }
            else
            {
                m_PlanNotes.text = "1F: 관리사무소 · 로비 · 1층 복도.\n두 계단 모두 1층 복도로 내려온다.\n관리사무소 문 주변은 무전 불통.";
            }
        }

        // ================================================================ 팩스
        void BindFax()
        {
            m_FaxMeta = m_Fax.Q<Label>("faxMeta");
            m_FaxCallSigns = m_Fax.Q<Label>("faxCallSigns");
            m_FaxRules = m_Fax.Q("faxRules");
            m_FaxCodes = m_Fax.Q("faxCodes");
            m_Fax.Q<Button>("faxClose").clicked += Close;
            for (int i = 0; i < ShiftFax.BasicRules.Length; i++)
            {
                var l = new Label($"{i + 1}. {ShiftFax.BasicRules[i]}");
                l.AddToClassList("fax-rule");
                m_FaxRules.Add(l);
            }
            m_FaxCallSigns.text = $"상황실 = '{ShiftFax.CallSignControl}' · 현장 = '{ShiftFax.CallSignField}'";
        }

        void RefreshFax()
        {
            var fax = ShiftFax.I;
            if (fax == null || !fax.IsSpawned || fax.PrintCount.Value == m_FaxShownPrint) return;
            m_FaxShownPrint = fax.PrintCount.Value;
            m_FaxMeta.text = $"{GameClock.Format(fax.PrintedAtMinute.Value)} 수신 · 오늘 밤 한정";
            m_FaxCodes.Clear();
            foreach (var code in fax.CodeList)
            {
                var row = new VisualElement();
                row.AddToClassList("fax-code-row");
                var c = new Label(code);
                c.AddToClassList("fax-code");
                var dots = new Label(ShiftFax.Dots(code));
                dots.AddToClassList("fax-dots");
                row.Add(c);
                row.Add(dots);
                m_FaxCodes.Add(row);
            }
        }

        // ================================================================ open / close / update
        void Open(OfficeScreen screen)
        {
            if (!m_Bound) return;
            Close();
            var local = PlayerNet.Local;
            if (screen == OfficeScreen.Terminal)
            {
                UIRoot.Show(m_Term, true);
                UIState.Push(TermKey);
                local?.SetFlag(PlayerNet.Flags.AtTerminal, true);
                m_CardLogCount = -1;
                m_RegistryStamp = uint.MaxValue;
                RefreshPlan();
            }
            else
            {
                m_FaxShownPrint = -1;
                RefreshFax();
                UIRoot.Show(m_Fax, true);
                UIState.Push(FaxKey);
                local?.SetFlag(PlayerNet.Flags.ReadingFax, true);
                AudioService.I?.PlayUi(SfxId.UiClick);
            }
            Refresh();
        }

        /// <summary>Frame an office screen was closed (so the same Esc press does not also open the pause menu).</summary>
        public static int LastClosedFrame { get; private set; } = -1;

        public void Close()
        {
            if (!m_Bound) return;
            var local = PlayerNet.Local;
            if (TerminalOpen || FaxOpen) LastClosedFrame = Time.frameCount;
            if (TerminalOpen)
            {
                UIRoot.Show(m_Term, false);
                UIState.Pop(TermKey);
                local?.SetFlag(PlayerNet.Flags.AtTerminal, false);
            }
            if (FaxOpen)
            {
                UIRoot.Show(m_Fax, false);
                UIState.Pop(FaxKey);
                local?.SetFlag(PlayerNet.Flags.ReadingFax, false);
            }
        }

        void Update()
        {
            if (!m_Bound)
            {
                if (UIRoot.I != null && UIRoot.I.Root != null) Bind(UIRoot.I.Root);
                if (!m_Bound) return;
            }
            SamplePower();
            if (!TerminalOpen && !FaxOpen) return;

            var local = PlayerNet.Local;
            if (local == null || local.Vanished.Value || (local.inputs != null && local.inputs.PausePressed))
            {
                Close();
                return;
            }
            if (Time.unscaledTime >= m_NextRefresh) Refresh();
        }

        void Refresh()
        {
            m_NextRefresh = Time.unscaledTime + 0.25f;
            if (FaxOpen) RefreshFax();
            if (!TerminalOpen) return;

            var clock = GameClock.I;
            m_Clock.text = clock != null && clock.IsSpawned && clock.Running.Value ? clock.Text : "--:--";
            bool powerOut = LightingNet.I != null && LightingNet.I.IsSpawned && LightingNet.I.OfficePowerOut.Value;
            UIRoot.Show(m_PowerOut, powerOut);
            m_Status.text = powerOut ? "상황실 전원 꺼짐" : "상황실 전원 정상";
            m_Status.EnableInClassList("alert", powerOut);
            RefreshRegistered();
            switch (m_Page)
            {
                case Page.Gauges:
                    RefreshGauges();
                    break;
                case Page.Remote:
                    RefreshRemote(powerOut);
                    break;
                case Page.Plan:
                    m_Plan.MarkDirtyRepaint();
                    break;
            }
        }

        int m_RegisteredCount = -1;

        void RefreshRegistered()
        {
            var dir = EntityDirector.I;
            if (dir == null || !dir.IsSpawned || dir.Log.Count == m_RegisteredCount) return;
            m_RegisteredCount = dir.Log.Count;
            while (m_Registered.childCount > 1) m_Registered.RemoveAt(1);
            var catalog = EntityCatalog.I;
            if (dir.Log.Count == 0)
            {
                var none = new Label("(없음)");
                none.AddToClassList("registered-item");
                none.AddToClassList("old");
                m_Registered.Add(none);
                return;
            }
            for (int i = dir.Log.Count - 1; i >= 0 && i >= dir.Log.Count - 3; i--)
            {
                var rec = dir.Log[i];
                var item = new Label($"{GameClock.Format(rec.Minute)}  {(catalog != null ? catalog.BundleImpression(rec.Bundle) : rec.Bundle.ToString())}");
                item.AddToClassList("registered-item");
                if (i != dir.Log.Count - 1) item.AddToClassList("old");
                m_Registered.Add(item);
            }
        }

        // ---------------------------------------------------------------- test hooks
        public void TestSelectTag(ClueAttr attr, string value)
        {
            m_Selected[attr] = value;
            RefreshManual();
        }

        public void TestClearTags()
        {
            m_Selected.Clear();
            RefreshManual();
        }

        public int TestCandidateCount => m_CandidateList != null ? m_CandidateList.childCount : -1;
        public string TestCardName => m_CardBody != null && !m_CardBody.ClassListContains("hidden") ? m_CardName.text : null;

        /// <summary>0 매뉴얼, 1 계기판, 2 원격 조작, 3 도면.</summary>
        public void TestShowPage(int page) => ShowPage((Page)Mathf.Clamp(page, 0, 3));

        public void TestPlanFloor(int floor)
        {
            m_Plan.Floor = floor;
            RefreshPlan();
        }

        public string TestRegisteredText
        {
            get
            {
                m_RegisteredCount = -1;
                RefreshRegistered();
                return m_Registered != null && m_Registered.childCount > 1 && m_Registered[1] is Label l ? l.text : null;
            }
        }
    }
}
