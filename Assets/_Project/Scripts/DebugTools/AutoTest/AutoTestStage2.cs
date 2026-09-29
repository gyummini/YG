using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        readonly List<(EncounterEvent ev, EntityId id, string detail)> m_EntityEvents = new List<(EncounterEvent, EntityId, string)>();

        void OnEntityEvent(EncounterEvent ev, EntityId id, string detail) => m_EntityEvents.Add((ev, id, detail));

        bool Saw(EncounterEvent ev, EntityId id)
        {
            foreach (var e in m_EntityEvents)
                if (e.ev == ev && e.id == id)
                    return true;
            return false;
        }

        string EventsText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in m_EntityEvents) sb.Append(e.ev).Append(':').Append(e.detail).Append(" | ");
            return sb.ToString();
        }

        /// <summary>Turn the field player's view toward a point (yaw + pitch), like moving the mouse.</summary>
        static void FieldLook(Vector3 target)
        {
            var f = FieldP;
            var d = target - f.HeadPosition;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            AutoTestNet.I.ClientLookRpc(yaw, pitch);
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        IEnumerator WaitHidden(float timeout)
        {
            float t0 = Time.time;
            while (TallFigure.I != null && TallFigure.I.Shown.Value && Time.time - t0 < timeout) yield return null;
        }

        IEnumerator ResetField(Vector3 pos, float yaw)
        {
            var field = FieldP;
            field.ServerRestore();
            FieldInput();
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, false);
            AutoTestNet.I.ClientDropFlashlightRpc(false);
            field.TeleportRpc(pos, yaw);
            AutoTestNet.I.ClientLookRpc(yaw, 0f);
            yield return new WaitForSeconds(1.2f);
        }

        /// <summary>Stage 2: control-room systems (terminal, fax, gauges, remote controls, plan) and bundles A, C.</summary>
        IEnumerator Stage2()
        {
            var field = FieldP;
            var control = ControlP;
            var dir = EntityDirector.I;
            var lights = LightingNet.I;
            var ui = OfficeScreens.Test;
            dir.ServerEvent += OnEntityEvent;
            TallEncounter.TestSpawnDistance = 11f;
            float y3 = L.FloorY(3);

            // ---------------------------------------------------------------- 0) shift start: the fax
            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            NightDirector.I.ServerStartNight(4242);
            yield return new WaitForSeconds(1.2f);
            var fax = ShiftFax.I;
            var codes = fax.CodeList;
            bool format = codes.Length == 3;
            foreach (var c in codes) format &= Regex.IsMatch(c, "^[1-3](-[1-3]){1,2}$");
            Check("교대 팩스: 밤 시작 시 출력", fax.Printed, $"print#{fax.PrintCount.Value}");
            Check("노크 암호 3개 · 서로 다름 · '2-1' 형식", format && codes[0] != codes[1] && codes[1] != codes[2] && codes[0] != codes[2], string.Join(", ", codes));
            Check("팩스는 두 역할 모두 읽을 수 있음", Object.FindAnyObjectByType<FaxInteract>()?.Prompt(field) == "팩스 읽기" && Object.FindAnyObjectByType<FaxInteract>()?.Prompt(control) == "팩스 읽기", "");
            OfficeScreens.Open(OfficeScreen.Fax);
            yield return new WaitForSeconds(0.8f);
            Check("팩스 화면 열림", ui != null && ui.FaxOpen, "");
            AutoTestNet.CaptureLocal("s2_fax.png");
            yield return new WaitForSeconds(0.5f);
            ui?.Close();

            // ---------------------------------------------------------------- 1) terminal + manual
            var term = Object.FindAnyObjectByType<TerminalInteract>();
            Check("단말기 안내는 상황실에게만", term != null && term.Prompt(control) == "단말기 사용" && term.Prompt(field) == null,
                term == null ? "TerminalInteract 없음" : $"상황실='{term.Prompt(control)}' 현장='{term.Prompt(field)}'");
            OfficeScreens.Open(OfficeScreen.Terminal);
            yield return new WaitForSeconds(0.8f);
            Check("단말기 열림 · 이동/시선 차단", ui != null && ui.TerminalOpen && UIState.BlocksGameplay, "");
            ui.TestShowPage(0);
            ui.TestClearTags();
            ui.TestSelectTag(ClueAttr.FirstImpression, "복도에서 키 큰 형체가 다가온다");
            yield return null;
            Check("매뉴얼: 첫인상 '키 큰 형체' → 후보 2 (대응은 아직 안 보임)", ui.TestCandidateCount == 2 && ui.TestCardName == null, $"{ui.TestCandidateCount} {ui.TestCardName}");
            ui.TestSelectTag(ClueAttr.Footsteps, "없음");
            yield return new WaitForSeconds(0.5f);
            Check("매뉴얼: + 발소리 없음 → 키다리 하나, 대응 카드", ui.TestCandidateCount == 1 && ui.TestCardName == "키다리", $"{ui.TestCandidateCount} {ui.TestCardName}");
            AutoTestNet.CaptureLocal("s2_terminal_manual_tallone.png");
            yield return new WaitForSeconds(0.4f);
            ui.TestClearTags();
            ui.TestSelectTag(ClueAttr.FirstImpression, "조명이 꺼진다");
            ui.TestSelectTag(ClueAttr.RadioNoise, "정상");
            yield return null;
            Check("매뉴얼: 조명이 꺼진다 + 무전 정상 → 누전", ui.TestCandidateCount == 1 && ui.TestCardName == "누전", $"{ui.TestCandidateCount} {ui.TestCardName}");
            ui.TestSelectTag(ClueAttr.FloorPower, "현장 쪽으로 차례로 떨어짐");
            yield return null;
            Check("매뉴얼: 서로 맞지 않는 태그 → 후보 없음", ui.TestCandidateCount == 0, ui.TestCandidateCount.ToString());
            ui.TestClearTags();
            ui.TestSelectTag(ClueAttr.FirstImpression, "조명이 꺼진다");
            ui.TestSelectTag(ClueAttr.FloorPower, "현장 쪽으로 차례로 떨어짐");
            yield return new WaitForSeconds(0.5f);
            Check("매뉴얼: 조명이 꺼진다 + 전력 차례로 떨어짐 → 불먹는 것", ui.TestCardName == "불먹는 것", ui.TestCardName ?? "-");
            AutoTestNet.CaptureLocal("s2_terminal_manual_lighteater.png");
            yield return new WaitForSeconds(0.4f);
            ui.TestShowPage(3);
            ui.TestPlanFloor(3);
            yield return new WaitForSeconds(0.6f);
            AutoTestNet.CaptureLocal("s2_terminal_plan_3F.png");
            yield return new WaitForSeconds(0.4f);
            ui.TestShowPage(2);
            yield return new WaitForSeconds(0.6f);
            AutoTestNet.CaptureLocal("s2_terminal_remote.png");
            yield return new WaitForSeconds(0.4f);
            ui.Close();
            Check("단말기 닫힘 · 조작 복귀", !ui.TerminalOpen && !UIState.Has("terminal"), "");

            // ---------------------------------------------------------------- 2) remote controls (from the control player)
            lights.ServerSetFloor(3, true);
            yield return new WaitForSeconds(0.8f);
            float kwOn = lights.Power.Value[3];
            lights.RemoteSetSectionRpc(3, L.SectionWest, false);
            yield return new WaitForSeconds(0.8f);
            float kwOff = lights.Power.Value[3];
            Check("원격 조명: 3층 서쪽 구간만 꺼짐", !lights.SectionOn(3, L.SectionWest) && lights.SectionOn(3, L.SectionEast), "");
            Check("층별 전력 계기판이 따라 떨어짐", kwOff < kwOn - 0.5f, $"{kwOn:0.00} → {kwOff:0.00} kW");
            lights.RemoteSetSectionRpc(3, L.SectionWest, true);

            var mid = Door.ByKey("fireMid3");
            mid.ServerOpen(0f);
            yield return new WaitForSeconds(0.6f);
            BuildingControl.I.RemoteLockRpc(new Unity.Collections.FixedString32Bytes("fireMid3"), true);
            yield return new WaitForSeconds(0.9f);
            Check("원격 방화문 잠금: 닫히고 잠김", !mid.IsOpen.Value && mid.Locked.Value, "");
            field.TeleportRpc(new Vector3(L.MidFireDoorX + 1.0f, y3, L.CorridorCenterZ), -90f);
            yield return new WaitForSeconds(1.0f);
            AutoTestNet.I.ClientDoorRpc("fireMid3", true);
            yield return new WaitForSeconds(1.0f);
            bool denied = CardLog.I.TryLast(out var rec) && !rec.Ok;
            Check("잠긴 방화문은 현장 카드로 안 열림 (카드 기록 '거부')", !mid.IsOpen.Value && denied, rec.Label.ToString());
            BuildingControl.I.RemoteLockRpc(new Unity.Collections.FixedString32Bytes("fireMid3"), false);
            yield return new WaitForSeconds(0.6f);
            AutoTestNet.I.ClientDoorRpc("fireMid3", true);
            yield return new WaitForSeconds(1.0f);
            Check("잠금 해제 후 카드로 열림", mid.IsOpen.Value && !mid.Locked.Value, "");

            var el = Elevator.I;
            el.RemoteCallRpc(3);
            float te = Time.time;
            while (Time.time - te < 20f && !(el.RestFloor == 3 && el.DoorState == ElevatorDoorState.Open)) yield return null;
            Check("원격 엘리베이터 호출 → 3층 도착·문 열림", el.RestFloor == 3 && el.DoorState == ElevatorDoorState.Open, $"{Time.time - te:0.0}s");

            // ---------------------------------------------------------------- 3) 출동 트리거: leaving the office registers a bundle
            field.TeleportRpc(OfficeB, 90f);
            yield return new WaitForSeconds(1.5f);
            m_EntityEvents.Clear();
            int logBefore = dir.Log.Count;
            bool wasIn = field.ZoneType == ZoneType.Office;
            field.TeleportRpc(new Vector3(2.5f, 0f, 6.0f), 90f);
            float tr = Time.time;
            while (Time.time - tr < 4f && dir.Current.Value == BundleId.None) yield return null;
            float took = Time.time - tr;
            yield return new WaitForSeconds(0.3f);
            var armed = dir.Armed;
            bool registered = dir.Current.Value != BundleId.None && dir.Log.Count == logBefore + 1;
            Check("현장이 문을 나서면 묶음 1개 등록", registered && armed != EntityId.None && armed != EntityId.Mimic,
                $"{dir.Current.Value} armed={armed} {took:0.00}s 사무실 안에서 출발={wasIn}");
            string shown = ui.TestRegisteredText;
            Check("단말기에는 첫인상만 표시 (어느 개체인지는 숨김)", shown != null && !shown.Contains("키다리") && !shown.Contains("배웅꾼") && !shown.Contains("불먹는") && !shown.Contains("누전"), shown ?? "-");
            field.TeleportRpc(OfficeB, 90f);
            yield return new WaitForSeconds(1.2f);
            Check("관리사무소로 돌아오면 정리됨", dir.Current.Value == BundleId.None && dir.Armed == EntityId.None && dir.Active == null, "");

            // ---------------------------------------------------------------- 4) 키다리 · 불 켜짐: eye contact, stand still → backs away
            var fig = TallFigure.I;
            var spot = new Vector3(-6f, y3, L.CorridorCenterZ);
            lights.ServerSetFloor(3, true);
            yield return ResetField(spot, 90f);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.TallOne);
            yield return new WaitForSeconds(0.3f);
            bool six = fig.Shown.Value && fig.Kind.Value == EntityId.TallOne;
            foreach (var r in fig.extraFingers) six &= r != null && r.enabled;
            Check("키다리 등장 · 손가락 6개 (여섯째 손가락 보임)", six, $"shown={fig.Shown.Value} extras={fig.extraFingers.Length}");
            float tw = Time.time;
            bool shot = false;
            while (Time.time - tw < 45f && !Saw(EncounterEvent.Resolved, EntityId.TallOne) && !field.Vanished.Value)
            {
                FieldLook(fig.HeadPosition);
                if (!shot && FlatDistance(fig.Feet, field.transform.position) < 3.2f)
                {
                    shot = true;
                    AutoTestNet.I.ClientCaptureRpc("s2_tallone_eye_contact.png");
                }
                yield return new WaitForSeconds(0.1f);
            }
            Check("키다리(불 켜짐): 눈 마주치고 버티면 물러남", Saw(EncounterEvent.Resolved, EntityId.TallOne) && !Saw(EncounterEvent.Warning, EntityId.TallOne) && !field.Vanished.Value,
                $"{Time.time - tw:0.0}s {EventsText()}");
            yield return WaitHidden(8f);

            // ---------------------------------------------------------------- 5) 키다리 · looking away: warning (neck crack, faster) then vanish
            yield return ResetField(spot, 90f);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.TallOne);
            tw = Time.time;
            while (Time.time - tw < 30f && !Saw(EncounterEvent.Vanish, EntityId.TallOne))
            {
                AutoTestNet.I.ClientLookRpc(-90f, 0f); // back turned
                yield return new WaitForSeconds(0.1f);
            }
            Check("키다리: 시선을 떼면 첫 실수는 경고(목 꺾이는 소리·빨라짐), 다음은 사라짐",
                Saw(EncounterEvent.Warning, EntityId.TallOne) && Saw(EncounterEvent.Vanish, EntityId.TallOne) && field.Vanished.Value, EventsText());
            float tv = Time.time;
            while (Time.time - tv < GameSettings.I.night.vanishToResultSec + 3f && NightDirector.I.Phase.Value == NightPhase.Running) yield return new WaitForSeconds(0.1f);
            Check("현장이 사라지면 잠시 뒤 밤이 끝남 (현장 실종)",
                NightDirector.I.Phase.Value == NightPhase.Ended && NightDirector.I.Outcome.Value == NightOutcome.FieldVanished, $"{Time.time - tv:0.0}s");
            yield return WaitHidden(8f);
            NightDirector.I.ServerStartNight(4243); // the rest of the suite runs in a fresh night
            yield return new WaitForSeconds(1.2f);

            // ---------------------------------------------------------------- 6) 키다리 · 불 꺼짐: head down until the control room lights the section
            lights.ServerSetSection(3, L.SectionWest, false);
            yield return ResetField(spot, 90f);
            FieldInput(headDown: true);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.TallOne);
            tw = Time.time;
            while (Time.time - tw < 30f && FlatDistance(fig.Feet, field.transform.position) > 2.8f && !field.Vanished.Value) yield return new WaitForSeconds(0.1f);
            bool waitedDark = !field.Vanished.Value && !Saw(EncounterEvent.Warning, EntityId.TallOne);
            lights.RemoteSetSectionRpc(3, L.SectionWest, true);
            yield return new WaitForSeconds(0.6f);
            FieldInput(headDown: false);
            tw = Time.time;
            while (Time.time - tw < 20f && !Saw(EncounterEvent.Resolved, EntityId.TallOne) && !field.Vanished.Value)
            {
                FieldLook(fig.HeadPosition);
                yield return new WaitForSeconds(0.1f);
            }
            Check("키다리(불 꺼짐): 고개 숙이고 버티다, 불이 켜지면 눈 마주쳐 물리침",
                waitedDark && Saw(EncounterEvent.Resolved, EntityId.TallOne) && !Saw(EncounterEvent.Warning, EntityId.TallOne) && !field.Vanished.Value, EventsText());
            yield return WaitHidden(8f);

            // ---------------------------------------------------------------- 7) 배웅꾼 · 벽 쪽: back to the wall, head down, still → passes
            var wallSpot = new Vector3(-6f, y3, L.CorridorSouthWallZ + 0.1f + 0.36f);
            yield return ResetField(wallSpot, 0f);
            FieldInput(headDown: true);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Escort);
            yield return new WaitForSeconds(0.3f);
            bool five = fig.Shown.Value && fig.Kind.Value == EntityId.Escort;
            foreach (var r in fig.extraFingers) five &= r != null && !r.enabled;
            Check("배웅꾼 등장 · 손가락 5개 (여섯째 손가락 없음)", five, "");
            tw = Time.time;
            while (Time.time - tw < 40f && !Saw(EncounterEvent.Resolved, EntityId.Escort) && !field.Vanished.Value) yield return new WaitForSeconds(0.1f);
            Check("배웅꾼(벽 쪽): 벽에 붙어 고개 숙이고 버티면 지나감", Saw(EncounterEvent.Resolved, EntityId.Escort) && !Saw(EncounterEvent.Warning, EntityId.Escort) && !field.Vanished.Value,
                $"{Time.time - tw:0.0}s {EventsText()}");
            FieldInput();
            yield return WaitHidden(8f);

            // ---------------------------------------------------------------- 8) 배웅꾼 · 한가운데: into the nearest empty room, head down, shut the door
            var probe = new Vector3(-6f, y3, L.CorridorCenterZ);
            UnitRegistry.I.TryNearestEmpty(3, probe, out var room);
            var start = L.PathPoint(Mathf.Clamp(room.PathPos + 3.5f, 1f, L.MidFireDoorPathPos - 1f), 3);
            yield return ResetField(start, -90f);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Escort);
            bool walked = false;
            yield return WalkField(room.OutsideDoor, 12f, (a, b) => walked = a, headDown: true);
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, true);
            yield return new WaitForSeconds(1.3f);
            yield return WalkField(room.InsideDoor - room.OutDir * 1.2f, 8f, (a, b) => walked &= a, headDown: true);
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, false);
            tw = Time.time;
            while (Time.time - tw < 40f && !Saw(EncounterEvent.Resolved, EntityId.Escort) && !field.Vanished.Value) yield return new WaitForSeconds(0.1f);
            Check($"배웅꾼(한가운데): 가장 가까운 빈방({room.Number}호)에 고개 숙이고 들어가 문 닫으면 지나감",
                Saw(EncounterEvent.Resolved, EntityId.Escort) && !field.Vanished.Value, $"walk={walked} {EventsText()}");
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, false);
            yield return WaitHidden(8f);

            // ---------------------------------------------------------------- 9) 불먹는 것 · 복도: floor dark, flashlight off, still for 10 s
            lights.ServerReviveAll();
            lights.ServerSetFloor(2, true);
            lights.ServerSetFloor(3, true);
            yield return ResetField(spot, 90f);
            yield return new WaitForSeconds(1.0f);
            float base2 = lights.Power.Value[2], base3 = lights.Power.Value[3];
            float t2 = -1f, t3 = -1f;
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.LightEater);
            var eater = dir.Active as LightEaterEncounter;
            tw = Time.time;
            while (Time.time - tw < 45f && (eater == null || !eater.Locked) && !field.Vanished.Value)
            {
                var pw = lights.Power.Value;
                if (t2 < 0f && pw[2] < base2 - 0.2f) t2 = Time.time - tw;
                if (t3 < 0f && pw[3] < base3 - 0.2f) t3 = Time.time - tw;
                yield return new WaitForSeconds(0.25f);
            }
            Check("불먹는 것: 층별 전력이 아래층부터 현장 층으로 차례로 떨어짐", t2 >= 0f && t3 >= 0f && t2 < t3, $"2F@{t2:0.0}s 3F@{t3:0.0}s");
            Check("불먹는 것: 다가올수록 무전 잡음", RadioNet.I.Noise.Value > 0.2f, $"noise={RadioNet.I.Noise.Value:0.00}");
            Check("불먹는 것: 복도 조건으로 확정", eater != null && eater.Locked && !eater.StairsCase, "");
            AutoTestNet.I.ClientLookRpc(-90f, 0f); // back toward the stairwell it came up
            yield return new WaitForSeconds(0.3f);
            AutoTestNet.I.ClientCaptureRpc("s2_lighteater_dark_corridor.png");
            lights.RemoteSetSectionRpc(3, L.SectionWest, false);
            lights.RemoteSetSectionRpc(3, L.SectionEast, false);
            tw = Time.time;
            while (Time.time - tw < 25f && !Saw(EncounterEvent.Resolved, EntityId.LightEater) && !field.Vanished.Value) yield return new WaitForSeconds(0.1f);
            Check("불먹는 것(복도): 층 조명 전부 끄고 손전등 끄고 10초 제자리 → 떠남",
                Saw(EncounterEvent.Resolved, EntityId.LightEater) && !Saw(EncounterEvent.Warning, EntityId.LightEater), $"{Time.time - tw:0.0}s {EventsText()}");
            Check("불먹는 것이 떠나면 무전 잡음도 사라짐", RadioNet.I.Noise.Value < 0.01f, $"{RadioNet.I.Noise.Value:0.00}");

            // ---------------------------------------------------------------- 10) 불먹는 것 · mistake: light kept on → the flashlight dies for good
            lights.ServerReviveAll();
            lights.ServerSetFloor(2, true);
            lights.ServerSetFloor(3, true);
            yield return ResetField(new Vector3(L.WestStairDoorX + 4f, y3, L.CorridorCenterZ), -90f);
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, true);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.LightEater);
            tw = Time.time;
            while (Time.time - tw < 45f && !Saw(EncounterEvent.Warning, EntityId.LightEater)) yield return new WaitForSeconds(0.1f);
            yield return new WaitForSeconds(0.5f);
            Check("불먹는 것: 첫 실수 → 현장 손전등이 꺼지고 다시 안 켜짐", Saw(EncounterEvent.Warning, EntityId.LightEater) && field.FlashDead.Value, EventsText());
            dir.ServerReset();
            lights.ServerReviveAll();

            // ---------------------------------------------------------------- 11) 누전: reset that floor's electric panel
            lights.ServerSetFloor(3, true);
            yield return ResetField(spot, 90f);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Short);
            yield return new WaitForSeconds(1.5f);
            Check("누전: 현장 층이 깜빡이고 전력이 무작위로 오르내림", lights.IsFaulted(3), "");
            var panel = L.PanelPosition(3);
            var facing = L.PanelFacing(3);
            field.TeleportRpc(new Vector3(panel.x, y3, panel.z) + facing * 0.9f, Mathf.Atan2(-facing.x, -facing.z) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(1.2f);
            FieldLook(panel);
            yield return new WaitForSeconds(0.4f);
            AutoTestNet.I.ClientCaptureRpc("s2_panel_3F.png");
            AutoTestNet.I.ClientPanelResetRpc(3);
            yield return new WaitForSeconds(1.5f);
            Check("누전: 3층 배전함 리셋으로 해결", !lights.IsFaulted(3) && Saw(EncounterEvent.Resolved, EntityId.Short), EventsText());

            // ---------------------------------------------------------------- 12) gauges after all that (card log, power)
            OfficeScreens.Open(OfficeScreen.Terminal);
            yield return new WaitForSeconds(0.6f);
            ui.TestShowPage(1);
            yield return new WaitForSeconds(1.0f);
            AutoTestNet.CaptureLocal("s2_terminal_gauges.png");
            yield return new WaitForSeconds(0.5f);
            ui.Close();

            TallEncounter.TestSpawnDistance = 0f;
            dir.ServerEvent -= OnEntityEvent;
            dir.ServerReset();
            field.ServerRestore();
            lights.ServerReviveAll();
            ReleaseInputs();
        }
    }
}
