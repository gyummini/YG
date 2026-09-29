using System.Collections;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        /// <summary>
        /// "한 밤이 끝까지 굴러가는가": a whole night on a shortened clock. Complaints arrive on the real schedule (scaled),
        /// the control room dispatches each one, the field walks out (a bundle is registered from the complaint), handles
        /// it and comes back through the office door (흉내쟁이 allowed; the control room only opens after a swipe + knock
        /// of an unused code). Entities that show up are left to the rules; if the field vanishes the night ends there.
        /// Checks the schedule, registration, handling, the return and the result screen.
        /// </summary>
        IEnumerator LoopSuite()
        {
            var n = GameSettings.I.night;
            var es = GameSettings.I.entities;
            float nightWas = n.realSecondsPerNight, firstWas = n.firstComplaintDelaySec, intervalWas = n.complaintIntervalSec;
            float mimicWas = es.mimicChance;
            n.realSecondsPerNight = 180f;
            n.firstComplaintDelaySec = 6f;
            n.complaintIntervalSec = 34f;
            MimicDirector.TestForce = null;
            es.mimicChance = 0.5f;

            var field = FieldP;
            var control = ControlP;
            var dir = EntityDirector.I;
            var board = ComplaintBoard.I;
            var night = NightDirector.I;
            dir.ServerEvent += OnEntityEvent;
            m_EntityEvents.Clear();
            int outings = 0, registered = 0, handled = 0, returns = 0, mimicKnocks = 0, opened = 0;
            dir.ServerEvent += (ev, id, d) =>
            {
                if (id == EntityId.Mimic && ev == EncounterEvent.Begin) mimicKnocks++;
            };

            control.TeleportRpc(new Vector3(-1.0f, 0f, 6.3f), 90f);
            field.TeleportRpc(OfficeB, 90f);
            night.ServerStartNight(5151);
            float t0 = Time.time;
            yield return new WaitForSeconds(1.2f);

            var codes = ShiftFax.I.CodeList;
            int codeIndex = 0;
            int lastSeen = 0;
            float firstArrival = -1f;
            while (night.Phase.Value == NightPhase.Running && Time.time - t0 < n.realSecondsPerNight + 20f)
            {
                // the control room answers every new complaint with [순찰 보냄]
                if (board.Items.Count > lastSeen)
                {
                    if (firstArrival < 0f) firstArrival = Time.time - t0;
                    var c = board.Items[board.Items.Count - 1];
                    board.ReplyRpc(c.Id, ComplaintState.Dispatched);
                    lastSeen = board.Items.Count;
                    Note($"{GameClock.Format(c.Minute)} 민원 {c.Unit}호 ({c.Bundle}) {ComplaintBoard.TextOf(c)}");
                }
                int open = -1;
                for (int i = 0; i < board.Items.Count; i++)
                    if (board.Items[i].State == ComplaintState.Dispatched) { open = i; break; }
                if (open < 0 || field.Vanished.Value)
                {
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // --- 출동: out through the office door into the lobby
                var item = board.Items[open];
                Door.ByKey("office").RequestToggleRpc();
                yield return new WaitForSeconds(0.5f);
                field.TeleportRpc(new Vector3(1.6f, 0.05f, 7.0f), 90f);
                yield return WaitUntil(() => dir.Current.Value != BundleId.None, 3f);
                outings++;
                if (dir.Current.Value == item.Bundle) registered++;
                Note($"출동 {item.Unit}호 → 등록 {dir.Current.Value} ({dir.Armed})");

                // --- the task
                if (item.Task == ComplaintTask.RideElevator)
                {
                    yield return FieldIntoCab(1);
                    AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, item.Floor);
                    float tw = Time.time;
                    bool stopped = false;
                    var el = Elevator.I;
                    while (Time.time - tw < 45f && board.Items[open].Open && !field.Vanished.Value)
                    {
                        // a team that knows its manual: 빈 층 in the dark → remote close; 동승자 going up → stop, walk out
                        if (dir.Active is EmptyFloorEncounter ef && ef.DoorsHeld && !LightingNet.I.FloorBright(ef.StopFloor, L.ElevatorHallSection) && Time.time - tw > 3f)
                            el.RemoteCloseRpc();
                        if (dir.Active is PassengerEncounter pe && pe.Riding && pe.GoingUp && !stopped)
                        {
                            stopped = true;
                            el.RemoteStopRpc();
                        }
                        if (dir.Active is PassengerEncounter pw && !pw.Riding && pw.GoingUp && el.DoorState == ElevatorDoorState.Open && FieldInCab && stopped)
                            FieldInput(move: new Vector2(0f, 1f));
                        else if (dir.Active == null && !FieldInCab && el.RestFloor != 0)
                        {
                            FieldInput();
                            yield return FieldIntoCab(el.RestFloor);
                            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, item.Floor);
                        }
                        else if (dir.Active == null && FieldInCab && el.RestFloor != 0 && el.RestFloor != item.Floor && el.DoorState == ElevatorDoorState.Open)
                            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, item.Floor);
                        yield return new WaitForSeconds(0.5f);
                    }
                    FieldInput();
                }
                else
                {
                    var u = UnitByNumber(item.Unit);
                    field.TeleportRpc(u.OutsideDoor + Vector3.up * 0.05f, Mathf.Atan2(-u.OutDir.x, -u.OutDir.z) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(1.5f);
                    AutoTestNet.I.ClientComplaintCheckRpc(item.Unit);
                    yield return WaitUntil(() => !board.Items[open].Open, 3f);
                }
                if (!board.Items[open].Open) handled++;
                if (field.Vanished.Value) break;

                // --- 복귀: card + knock (an unused code), the control room opens only for that
                yield return new WaitForSeconds(Random.Range(1f, 3f));
                field.TeleportRpc(new Vector3(1.6f, 0.05f, 7.0f), -90f);
                yield return new WaitForSeconds(1.0f);
                string code = codes[Mathf.Min(codeIndex++, codes.Length - 1)];
                AutoTestNet.I.ClientDoorRpc("office", true); // swipe at the office door: card record
                yield return new WaitForSeconds(0.4f);
                AutoTestNet.I.ClientKnockCodeRpc(code);
                yield return WaitUntil(() => KnockLog.LastFieldPattern.HasValue && KnockLog.LastFieldPattern.Value.Code == code, 8f);
                Door.ByKey("office").RequestToggleRpc();
                yield return new WaitForSeconds(0.6f);
                field.TeleportRpc(OfficeB, 90f);
                yield return WaitUntil(() => field.ZoneType == ZoneType.Office, 3f);
                if (field.ZoneType == ZoneType.Office) returns++;
                if (LightingNet.I.OfficePowerOut.Value) opened++;
                yield return new WaitForSeconds(1.0f);
                if (Door.ByKey("office").IsOpen.Value) Door.ByKey("office").RequestToggleRpc();
            }
            yield return WaitUntil(() => night.Phase.Value == NightPhase.Ended, n.realSecondsPerNight + 30f - (Time.time - t0));
            yield return new WaitForSeconds(1.0f);

            var rs = OfficeScreens.Results;
            bool vanished = night.Outcome.Value == NightOutcome.FieldVanished;
            Note($"events: {EventsText()}");
            Check("한 밤: 민원이 일정대로 도착 (첫 민원 · 간격)", board.Items.Count >= 3 && firstArrival > 0f && firstArrival < n.firstComplaintDelaySec + 3f,
                $"{board.Items.Count}건, 첫 민원 {firstArrival:0.0}s");
            Check("한 밤: 출동할 때마다 그 민원의 묶음이 등록됨", outings >= 1 && registered == outings, $"출동 {outings} · 민원 묶음 등록 {registered}");
            Check("한 밤: 민원 처리 → 복귀(카드 + 노크 → 상황실이 열어 줌)", handled >= 1 && (vanished || returns >= handled), $"처리 {handled} · 복귀 {returns} · 흉내쟁이 노크 {mimicKnocks}");
            Check("한 밤: 끝까지 굴러가 결과 화면 (04:00 퇴근 또는 현장 실종)", night.Phase.Value == NightPhase.Ended && rs != null && rs.Shown,
                $"{night.Outcome.Value} · {(rs != null ? rs.CountText : "-")} · {GameClock.I.Text}");
            Check("한 밤: 흉내쟁이에게 열어 준 적 없음 (상황실은 카드 + 새 암호일 때만 엶)", opened == 0, $"power-outs={opened}");
            AutoTestNet.CaptureLocal("loop_results.png");
            yield return new WaitForSeconds(0.5f);

            night.RequestBackToLobbyRpc();
            dir.ServerEvent -= OnEntityEvent;
            dir.ServerReset();
            field.ServerRestore();
            LightingNet.I.ServerReviveAll();
            n.realSecondsPerNight = nightWas;
            n.firstComplaintDelaySec = firstWas;
            n.complaintIntervalSec = intervalWas;
            es.mimicChance = mimicWas;
            MimicDirector.TestForce = null;
            ReleaseInputs();
        }
    }
}
