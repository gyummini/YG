using System.Collections;
using UnityEngine;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        IEnumerator WaitUntil(System.Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (!cond() && Time.time - t0 < timeout) yield return new WaitForSeconds(0.1f);
        }

        IEnumerator WaitEvent(EncounterEvent ev, EntityId id, float timeout) => WaitUntil(() => Saw(ev, id), timeout);

        /// <summary>Cab parked at a floor with its doors shut, the field inside facing the doors.</summary>
        IEnumerator FieldIntoCab(int floor)
        {
            var el = Elevator.I;
            el.ServerResetTo(floor);
            yield return new WaitForSeconds(0.3f);
            FieldP.TeleportRpc(new Vector3(6f, L.FloorY(floor) + 0.05f, 11.3f), 180f);
            AutoTestNet.I.ClientLookRpc(180f, 0f);
            yield return new WaitForSeconds(1.0f);
        }

        static bool FieldInCab => Elevator.I != null && FieldP != null && Elevator.I.CabContains(FieldP.transform.position + Vector3.up * 0.5f);

        IEnumerator WaitDoorsOpenAt(int floor, float timeout) =>
            WaitUntil(() => Elevator.I.RestFloor == floor && Elevator.I.DoorState == ElevatorDoorState.Open, timeout);

        static int OccupiedUnit(int floor, int skip = 0)
        {
            foreach (var u in L.UnitsOnFloor(floor))
                if (!UnitRegistry.I.IsEmptyRoom(u.Number) && u.Number != skip)
                    return u.Number;
            return floor * 100 + 1;
        }

        static L.UnitInfo UnitByNumber(int number)
        {
            foreach (var u in L.Units)
                if (u.Number == number)
                    return u;
            return L.Units[0];
        }

        /// <summary>Stage 3: complaints, bundles B and D, 흉내쟁이, the night loop and the result screen.</summary>
        IEnumerator Stage3()
        {
            var field = FieldP;
            var control = ControlP;
            var dir = EntityDirector.I;
            var lights = LightingNet.I;
            var el = Elevator.I;
            var board = ComplaintBoard.I;
            var night = NightDirector.I;
            var ui = OfficeScreens.Test;
            var es = GameSettings.I.entities;
            dir.ServerEvent += OnEntityEvent;
            MimicDirector.TestForce = false;
            float graceWas = es.followerRuleGrace;

            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            night.ServerStartNight(4343);
            board.ServerPauseSchedule();
            yield return new WaitForSeconds(1.2f);

            // ================================================================ 1) 민원 메신저
            int unitC = OccupiedUnit(3);
            var c1 = board.ServerArrive(BundleId.C, unitC);
            OfficeScreens.Open(OfficeScreen.Terminal);
            yield return new WaitForSeconds(0.6f);
            ui.TestShowPage(4);
            yield return new WaitForSeconds(0.4f);
            Check("민원 도착: 단말기 민원 탭에 1건 (답장 전)", ui.TestComplaintRows == 1 && board.Items[0].State == ComplaintState.New,
                $"{unitC}호 \"{ComplaintBoard.TextOf(c1)}\"");
            Check("민원 문구에 다음 상황의 힌트 (묶음 C: 조명)", c1.Bundle == BundleId.C && ComplaintBoard.TextOf(c1).Length > 0, ComplaintBoard.TextOf(c1));
            bool clicked = ui.TestReply(c1.Id, true);
            yield return new WaitForSeconds(0.6f);
            Check("[순찰 보냄] 버튼 → 출동 대기", clicked && board.Items[0].State == ComplaintState.Dispatched, "");
            var c2 = board.ServerArrive(BundleId.A, OccupiedUnit(4));
            yield return new WaitForSeconds(0.3f);
            clicked = ui.TestReply(c2.Id, false);
            yield return new WaitForSeconds(0.6f);
            Check("[기다려 주세요] 버튼 → 기다리는 중", clicked && board.Items[1].State == ComplaintState.Waiting, "");
            AutoTestNet.CaptureLocal("s3_terminal_complaints.png");
            yield return new WaitForSeconds(0.4f);
            ui.Close();

            field.TeleportRpc(new Vector3(3.2f, 0f, 7.0f), 90f); // out of the office door into the lobby
            yield return WaitUntil(() => dir.Current.Value != BundleId.None, 4f);
            Check("현장이 나가면 순찰 보낸 민원의 묶음(C)이 등록됨", dir.Current.Value == BundleId.C, $"{dir.Current.Value} armed={dir.Armed}");
            dir.ServerClearOnReturn();

            var uc = UnitByNumber(unitC);
            field.TeleportRpc(uc.OutsideDoor + Vector3.up * 0.05f, Mathf.Atan2(-uc.OutDir.x, -uc.OutDir.z) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(1.2f);
            dir.ServerClearOnReturn();
            AutoTestNet.I.ClientComplaintCheckRpc(unitC);
            yield return WaitUntil(() => board.Items[0].State == ComplaintState.Handled, 4f);
            Check("세대 문 앞 '민원 확인'으로 처리 · 처리 민원 1", board.Items[0].State == ComplaintState.Handled && night.HandledComplaints.Value == 1,
                $"handled={night.HandledComplaints.Value}");

            int unitB = OccupiedUnit(4, c2.Unit);
            var c3 = board.ServerArrive(BundleId.B, unitB);
            Check("엘리베이터 민원(묶음 B)은 '타고 그 층까지'", c3.Task == ComplaintTask.RideElevator, ComplaintBoard.TextOf(c3));
            yield return FieldIntoCab(1);
            dir.ServerClearOnReturn();
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 4);
            yield return WaitUntil(() => board.Items[2].State == ComplaintState.Handled, 20f);
            Check("엘리베이터로 그 층(4층)까지 가면 처리 · 처리 민원 2", board.Items[2].State == ComplaintState.Handled && night.HandledComplaints.Value == 2,
                $"rest={el.RestFloor} handled={night.HandledComplaints.Value}");

            // ================================================================ 2) 빈 층
            lights.ServerSetSection(3, L.ElevatorHallSection, true);
            ElevatorEncounter.TestStopFloor = 3;
            yield return FieldIntoCab(1);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.EmptyFloor);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 4);
            yield return WaitDoorsOpenAt(3, 15f);
            Check("빈 층: 누르지 않은 층(3층)에서 문이 열림", el.RestFloor == 3 && el.DoorState == ElevatorDoorState.Open && Saw(EncounterEvent.Begin, EntityId.EmptyFloor), EventsText());
            Check("빈 층: 탑승 인원 = 현장 인원 (1명) · 거울에 더 없음", el.Occupancy == 1 && !el.PassengerAboard.Value, $"occupancy={el.Occupancy}");
            AutoTestNet.I.ClientLookRpc(-90f, 0f);
            yield return new WaitForSeconds(0.6f);
            AutoTestNet.I.ClientCaptureRpc("s3_mirror_emptyfloor.png");
            yield return new WaitForSeconds(0.4f);
            AutoTestNet.I.ClientLookRpc(180f, 0f);
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.EmptyFloor, 16f);
            Check("빈 층(밝음): 버튼을 누르지 않고 기다리면 문이 스스로 닫히고 해결",
                Saw(EncounterEvent.Resolved, EntityId.EmptyFloor) && !Saw(EncounterEvent.Warning, EntityId.EmptyFloor), EventsText());
            yield return WaitDoorsOpenAt(4, 12f);
            Check("그 뒤 원래 누른 층(4층)으로 감", el.RestFloor == 4 && FieldInCab, $"rest={el.RestFloor}");

            lights.ServerSetSection(2, L.ElevatorHallSection, false);
            ElevatorEncounter.TestStopFloor = 2;
            yield return FieldIntoCab(4);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.EmptyFloor);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 1);
            yield return WaitDoorsOpenAt(2, 15f);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.Close, 0);
            yield return WaitEvent(EncounterEvent.Warning, EntityId.EmptyFloor, 3f);
            yield return new WaitForSeconds(1.8f);
            Check("빈 층: 버튼을 누르면 첫 실수는 경고 — 문이 다시 열림",
                Saw(EncounterEvent.Warning, EntityId.EmptyFloor) && el.DoorState == ElevatorDoorState.Open && el.RestFloor == 2 && !field.Vanished.Value, EventsText());
            yield return new WaitForSeconds(3f);
            Check("빈 층(어두움): 문이 스스로 닫히지 않음", el.DoorState == ElevatorDoorState.Open && el.RestFloor == 2, $"{el.DoorState}");
            el.RemoteCloseRpc();
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.EmptyFloor, 6f);
            Check("빈 층(어두움): 상황실이 원격으로 닫으면 해결", Saw(EncounterEvent.Resolved, EntityId.EmptyFloor), EventsText());
            yield return WaitDoorsOpenAt(1, 12f);

            lights.ServerSetSection(3, L.ElevatorHallSection, true);
            ElevatorEncounter.TestStopFloor = 3;
            yield return FieldIntoCab(1);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.EmptyFloor);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 4);
            yield return WaitDoorsOpenAt(3, 15f);
            yield return new WaitForSeconds(1f);
            el.RemoteCloseRpc();
            yield return WaitEvent(EncounterEvent.Warning, EntityId.EmptyFloor, 3f);
            Check("빈 층(밝음): 상황실이 원격으로 닫으면 실수 (대응 충돌 → 경고)", Saw(EncounterEvent.Warning, EntityId.EmptyFloor), EventsText());
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.EmptyFloor, 16f);
            Check("경고 뒤에는 다시 기다리면 문이 스스로 닫히고 해결", Saw(EncounterEvent.Resolved, EntityId.EmptyFloor) && !field.Vanished.Value, EventsText());
            yield return WaitDoorsOpenAt(4, 12f);

            // ================================================================ 3) 동승자
            ElevatorEncounter.TestStopFloor = 3;
            PassengerEncounter.TestGoUp = null;
            yield return FieldIntoCab(4);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Passenger);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 1);
            yield return WaitDoorsOpenAt(3, 15f);
            yield return new WaitForSeconds(0.3f);
            Check("동승자: 누르지 않은 층(3층)에서 문이 열리고 탑승 인원이 1명 많음 (2명)", el.PassengerAboard.Value && el.Occupancy == 2 && Saw(EncounterEvent.Begin, EntityId.Passenger),
                $"occupancy={el.Occupancy} {EventsText()}");
            var pass = dir.Active as PassengerEncounter;
            Check("동승자: 3층에서 탔으니 내려가는 중", pass != null && !pass.GoingUp, "");
            // look straight at its eyes in the mirror (still boarding: no rules yet) — the mirror view and the gaze math
            var head = el.passengerHead.position;
            var mc = el.mirror.position;
            var mn = -el.mirror.forward;
            var image = head - 2f * Vector3.Dot(head - mc, mn) * mn;
            FieldLook(image);
            yield return new WaitForSeconds(0.5f);
            AutoTestNet.I.ClientCaptureRpc("s3_mirror_passenger.png");
            Check("거울 속 동승자의 눈을 보면 판정됨 (탑승 중이라 아직 실수 아님)", PassengerEncounter.MeetsEyesInMirror(field) && !Saw(EncounterEvent.Warning, EntityId.Passenger), "");
            yield return new WaitForSeconds(0.3f);
            AutoTestNet.I.ClientLookRpc(180f, 0f);
            yield return WaitUntil(() => pass != null && pass.Riding, 10f);
            yield return new WaitForSeconds(es.passengerRuleGrace + 0.3f);
            FieldLook(el.passengerHead.position);
            yield return WaitEvent(EncounterEvent.Warning, EntityId.Passenger, 3f);
            AutoTestNet.I.ClientLookRpc(180f, 0f);
            Check("동승자: 돌아보면 첫 실수는 경고 — 귀가 숨소리 + 엘리베이터 멈춤", Saw(EncounterEvent.Warning, EntityId.Passenger) && !field.Vanished.Value,
                $"halted={el.State.Value.Halted} {EventsText()}");
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.Passenger, 20f);
            Check("동승자(내려가는 중): 그 뒤로 말하지 않고 돌아보지 않으면 1층까지 그대로 내려가 해결",
                Saw(EncounterEvent.Resolved, EntityId.Passenger) && el.RestFloor == 1 && !field.Vanished.Value, EventsText());
            yield return new WaitForSeconds(0.3f);
            Check("동승자가 내리면 탑승 인원이 돌아옴", !el.PassengerAboard.Value && el.Occupancy == 1, $"occupancy={el.Occupancy}");

            ElevatorEncounter.TestStopFloor = 2;
            PassengerEncounter.TestGoUp = true;
            yield return FieldIntoCab(1);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Passenger);
            AutoTestNet.I.ClientElevatorPressRpc(ElevatorButtonKind.CarFloor, 3);
            yield return WaitDoorsOpenAt(2, 15f);
            pass = dir.Active as PassengerEncounter;
            Check("동승자: 2층에서 타서 올라가는 중 (가장 가까운 층 = 3층)", pass != null && pass.GoingUp && pass.NearestFloor == 3, EventsText());
            yield return WaitUntil(() => pass != null && pass.Riding, 10f);
            yield return new WaitForSeconds(es.passengerRuleGrace + 0.3f);
            FieldInput(ptt: true);
            yield return WaitEvent(EncounterEvent.Warning, EntityId.Passenger, 3f);
            FieldInput();
            bool halted = el.State.Value.Halted;
            Check("동승자: 무전으로 말하면 첫 실수는 경고 — 엘리베이터가 층 사이에 멈춤", Saw(EncounterEvent.Warning, EntityId.Passenger) && halted, $"halted={halted} {EventsText()}");
            el.RemoteStopRpc();
            yield return WaitDoorsOpenAt(3, 15f);
            AutoTestNet.I.ClientLookRpc(180f, 0f);
            FieldInput(move: new Vector2(0f, 1f));
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.Passenger, 8f);
            FieldInput();
            Check("동승자(올라가는 중): 상황실이 정지로 가장 가까운 층에 세우고, 앞으로 걸어 나가면 해결",
                Saw(EncounterEvent.Resolved, EntityId.Passenger) && el.RestFloor == 3 && !FieldInCab && !field.Vanished.Value, EventsText());
            PassengerEncounter.TestGoUp = null;
            ElevatorEncounter.TestStopFloor = 0;

            // ================================================================ 4) 뒷사람
            dir.ServerClearOnReturn();
            lights.ServerSetFloor(3, true);
            es.followerRuleGrace = 12f; // time for the shadow picture before the rules start
            var start = L.PathPoint(4f, 3);
            field.TeleportRpc(start + Vector3.up * 0.05f, 90f);
            AutoTestNet.I.ClientLookRpc(90f, 0f);
            yield return new WaitForSeconds(1.2f);
            m_EntityEvents.Clear();
            FieldInput(move: new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.6f);
            dir.ServerForce(EntityId.Follower);
            int steps0 = Follower.FollowerSteps;
            yield return new WaitForSeconds(4.5f);
            float gap = Vector3.Distance(Follower.I.transform.position, field.transform.position);
            Check("뒷사람: 걷는 현장 뒤로 발소리가 따라붙음", Follower.FollowerSteps - steps0 >= 6 && gap < es.followDistance + 1.2f,
                $"steps={Follower.FollowerSteps - steps0} gap={gap:0.0}m");
            FieldInput();
            yield return new WaitForSeconds(1.2f);
            float lag = Follower.LastFollowerStepAt - Follower.LastFieldStepAt;
            Check("뒷사람: 멈추면 발소리도 즉시 멈춤", lag >= -0.01f && lag < 0.2f, $"마지막 발소리 차이 {lag:0.00}s");
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, true);
            FieldLook(Follower.I.transform.position + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.8f);
            AutoTestNet.I.ClientCaptureRpc("s3_follower_shadow.png");
            yield return new WaitForSeconds(0.4f);
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, false);
            Check("뒷사람: 몸은 그림자만 드리움 (손전등 빛에 그림자 하나 더)", Follower.I.Mode.Value == FollowMode.Follower && Follower.I.shadowBody.Length > 0 && Follower.I.shadowBody[0].enabled &&
                Follower.I.shadowBody[0].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly, "");
            yield return WaitUntil(() => (dir.Active as FollowerEncounter) == null || dir.Active.Age >= es.followerRuleGrace + 0.3f, 10f);
            FieldInput(ptt: true);
            yield return new WaitForSeconds(0.7f);
            FieldInput();
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.Follower, es.followerFadeSec + 4f);
            Check("뒷사람(무전 누름): 즉시 손을 떼고 제자리에서 버티면 발소리가 사라짐 (해결)",
                Saw(EncounterEvent.Resolved, EntityId.Follower) && !Saw(EncounterEvent.Warning, EntityId.Follower), EventsText());
            es.followerRuleGrace = graceWas;
            yield return new WaitForSeconds(2.5f);

            UnitRegistry.I.TryNearestEmpty(3, L.PathPoint(20f, 3), out var room);
            start = L.PathPoint(Mathf.Max(2f, room.PathPos - 14f), 3);
            field.TeleportRpc(start + Vector3.up * 0.05f, 90f);
            AutoTestNet.I.ClientLookRpc(90f, 0f);
            yield return new WaitForSeconds(1.2f);
            m_EntityEvents.Clear();
            FieldInput(move: new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.5f);
            dir.ServerForce(EntityId.Follower);
            yield return new WaitForSeconds(es.followerRuleGrace + 0.5f);
            FieldInput();
            yield return WaitEvent(EncounterEvent.Warning, EntityId.Follower, es.followerStillTolerance + 3f);
            yield return new WaitForSeconds(1.0f);
            gap = Vector3.Distance(Follower.I.transform.position, field.transform.position);
            Check("뒷사람(안 누름): 멈춰 서면 첫 실수는 경고 — 바로 뒤에 붙고 무전으로도 들림",
                Saw(EncounterEvent.Warning, EntityId.Follower) && RadioNet.I.ForcedRelayFrom.Value == field.OwnerClientId && gap < 1.6f, $"gap={gap:0.0}m {EventsText()}");
            bool walked = false;
            yield return WalkField(room.OutsideDoor, 20f, (a, b) => walked = a);
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, true);
            yield return new WaitForSeconds(0.6f);
            yield return WalkField(room.InsideDoor - room.OutDir * 1.2f, 8f, (a, b) => walked &= a);
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, false);
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.Follower, 6f);
            Check($"뒷사람(안 누름): 걸어서 가장 가까운 빈방({room.Number}호)에 들어가 문을 닫으면 해결",
                Saw(EncounterEvent.Resolved, EntityId.Follower) && !field.Vanished.Value, $"walk={walked} {EventsText()}");
            Check("해결되면 무전 강제 중계도 끝남", RadioNet.I.ForcedRelayFrom.Value == RadioNet.None, "");
            AutoTestNet.I.ClientDoorRpc("unit" + room.Number, false);
            yield return new WaitForSeconds(2.5f);

            // ================================================================ 5) 울림
            dir.ServerClearOnReturn();
            field.TeleportRpc(L.WestStair.LandingCenter(2) + Vector3.up * 0.05f, 0f);
            yield return new WaitForSeconds(1.2f);
            m_EntityEvents.Clear();
            dir.ServerForce(EntityId.Echo);
            int echo0 = Follower.EchoSteps;
            walked = false;
            yield return WalkField(L.WestStair.LandingCenter(3), 20f, (a, b) => walked = a);
            FieldInput();
            yield return new WaitForSeconds(1.5f);
            float tail = Follower.LastEchoStepAt - Follower.LastFieldStepAt;
            Check("울림: 계단에서 발소리가 따라옴 (현장 발소리 뒤 메아리)", Follower.EchoSteps - echo0 >= 4 && Saw(EncounterEvent.Begin, EntityId.Echo), $"echo={Follower.EchoSteps - echo0} walk={walked}");
            Check("울림: 멈추면 한 박자 늦게 잦아듦 (마지막 울림이 멈춘 뒤에 옴)", tail > 0.4f && tail < 1.2f, $"+{tail:0.00}s");
            Check("울림: 몸이 없어 그림자도 없음", Follower.I.Mode.Value == FollowMode.Echo && !Follower.I.shadowBody[0].enabled, "");
            field.TeleportRpc(L.WestStair.Door(3) + L.WestStair.OutDir * 1.2f + Vector3.up * 0.05f, 90f);
            yield return WaitEvent(EncounterEvent.Resolved, EntityId.Echo, 4f);
            Check("울림: 무시하고 계단실을 벗어나면 끝 (경고·실수 없음)",
                Saw(EncounterEvent.Resolved, EntityId.Echo) && !Saw(EncounterEvent.Warning, EntityId.Echo) && !Saw(EncounterEvent.Vanish, EntityId.Echo), EventsText());

            // ================================================================ 6) 흉내쟁이
            dir.ServerClearOnReturn();
            string code0 = ShiftFax.I.CodeList[0];
            control.TeleportRpc(new Vector3(-1.0f, 0f, 6.3f), 90f);
            field.TeleportRpc(new Vector3(1.0f, 0.05f, 7.0f), -90f);
            yield return new WaitForSeconds(1.2f);
            AutoTestNet.I.ClientKnockCodeRpc(code0);
            yield return WaitUntil(() => KnockLog.LastFieldPattern.HasValue && KnockLog.LastFieldPattern.Value.Code == code0, 8f);
            Check($"현장 노크가 관리사무소 문에 기록됨 ({code0})", KnockLog.LastFieldPattern.HasValue && KnockLog.LastFieldPattern.Value.Code == code0,
                KnockLog.LastFieldPattern.HasValue ? KnockLog.LastFieldPattern.Value.Code : "-");

            int cardsBefore = CardLog.I.Records.Count;
            MimicDirector.I.ServerReset(); // a fresh outing: the field's walk back down to the lobby above used up the roll
            MimicDirector.TestForce = true;
            int unitA = OccupiedUnit(2);
            var c4 = board.ServerArrive(BundleId.A, unitA);
            var ua = UnitByNumber(unitA);
            field.TeleportRpc(ua.OutsideDoor + Vector3.up * 0.05f, Mathf.Atan2(-ua.OutDir.x, -ua.OutDir.z) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(1.2f);
            dir.ServerClearOnReturn();
            m_EntityEvents.Clear();
            AutoTestNet.I.ClientComplaintCheckRpc(unitA);
            yield return WaitEvent(EncounterEvent.Begin, EntityId.Mimic, es.mimicDelaySec.y + 4f);
            dir.ServerClearOnReturn();
            Check("흉내쟁이: 현장이 민원을 처리하고 돌아오기 시작하면 먼저 와서 노크", Saw(EncounterEvent.Begin, EntityId.Mimic) && MimicDirector.I.AtDoor, EventsText());
            yield return new WaitForSeconds(6.5f); // one round of knocks, then the pattern closes after a silence
            var lastKnock = KnockLog.Patterns.Count > 0 ? KnockLog.Patterns[KnockLog.Patterns.Count - 1] : default;
            Check($"흉내쟁이: 들었던 노크({code0})를 그대로 따라 함", lastKnock.ByMimic && lastKnock.Code == code0 && MimicDirector.I.LastPattern == code0,
                $"{lastKnock.Code} mimic={lastKnock.ByMimic}");
            Check("흉내쟁이: 카드 기록은 남기지 않음", CardLog.I.Records.Count == cardsBefore, $"{cardsBefore}→{CardLog.I.Records.Count}");
            Check("문을 열지 않는 동안은 상황실 전원 정상", !lights.OfficePowerOut.Value, "");
            byte maskBefore = lights.SwitchMask.Value;
            Door.ByKey("office").RequestToggleRpc();
            yield return WaitUntil(() => lights.OfficePowerOut.Value, 3f);
            Check("흉내쟁이에게 문을 열면 상황실 전원이 꺼짐", lights.OfficePowerOut.Value && Saw(EncounterEvent.Opened, EntityId.Mimic),
                $"{lights.OfficePowerBackIn:0}s 뒤 복구 {EventsText()}");
            lights.RemoteSetSectionRpc(4, L.SectionWest, (maskBefore & (1 << L.Circuit(4, L.SectionWest))) == 0);
            yield return new WaitForSeconds(0.5f);
            Check("전원이 꺼진 동안 원격 조작 먹통", lights.SwitchMask.Value == maskBefore, "");
            OfficeScreens.Open(OfficeScreen.Terminal);
            yield return new WaitForSeconds(0.8f);
            AutoTestNet.CaptureLocal("s3_terminal_power_out.png");
            yield return new WaitForSeconds(0.4f);
            ui.Close();
            lights.ServerReviveAll();
            Door.ByKey("office").ServerClose();
            MimicDirector.TestForce = false;

            // manual card for 동승자 (report picture)
            OfficeScreens.Open(OfficeScreen.Terminal);
            yield return new WaitForSeconds(0.6f);
            ui.TestShowPage(0);
            ui.TestClearTags();
            ui.TestSelectTag(ClueAttr.FirstImpression, "엘리베이터가 누르지 않은 층에서 열린다");
            ui.TestSelectTag(ClueAttr.CabCount, "1명 많음");
            yield return new WaitForSeconds(0.6f);
            Check("매뉴얼: 엘리베이터 첫인상 + 탑승 인원 1명 많음 → 동승자", ui.TestCandidateCount == 1 && ui.TestCardName == "동승자", ui.TestCardName);
            AutoTestNet.CaptureLocal("s3_terminal_manual_passenger.png");
            yield return new WaitForSeconds(0.5f); // the capture is written at the end of a later frame
            ui.TestClearTags();
            ui.TestSelectTag(ClueAttr.FirstImpression, "뒤에서 발소리가 따라온다");
            ui.TestSelectTag(ClueAttr.Footsteps, "멈추면 한 박자 늦게 잦아듦");
            yield return new WaitForSeconds(0.4f);
            Check("매뉴얼: 발소리 첫인상 + 한 박자 늦게 잦아듦 → 울림", ui.TestCandidateCount == 1 && ui.TestCardName == "울림", ui.TestCardName);
            AutoTestNet.CaptureLocal("s3_terminal_manual_echo.png");
            yield return new WaitForSeconds(0.5f);
            ui.TestClearTags();
            ui.Close();

            // ================================================================ 7) 한 밤 루프 · 결과 화면
            int handled = night.HandledComplaints.Value;
            GameClock.I.ServerSetMinutes(GameClock.NightMinutes - 0.2f);
            yield return WaitUntil(() => night.Phase.Value == NightPhase.Ended, 5f);
            yield return new WaitForSeconds(1.0f);
            var rs = OfficeScreens.Results;
            Check("04:00이 되면 퇴근 → 결과 화면", night.Phase.Value == NightPhase.Ended && night.Outcome.Value == NightOutcome.Completed && rs != null && rs.Shown && rs.TitleText == "퇴근",
                rs != null ? rs.TitleText : "-");
            Check($"결과 화면: 처리한 민원 {handled} / {GameSettings.I.night.targetComplaints}", rs != null && rs.CountText == $"처리한 민원 {handled} / {GameSettings.I.night.targetComplaints}",
                rs != null ? rs.CountText : "-");
            AutoTestNet.CaptureLocal("s3_results.png");
            yield return new WaitForSeconds(0.5f);

            night.RequestStartNightRpc();
            yield return WaitUntil(() => night.Phase.Value == NightPhase.Running, 4f);
            board.ServerPauseSchedule();
            yield return new WaitForSeconds(1.0f);
            Check("다음 밤 시작 → 결과 화면 닫힘 · 민원 초기화", night.Phase.Value == NightPhase.Running && rs != null && !rs.Shown && board.Items.Count == 0 && night.HandledComplaints.Value == 0, "");
            dir.ServerVanishField(field, null, "test");
            yield return WaitUntil(() => night.Phase.Value == NightPhase.Ended, GameSettings.I.night.vanishToResultSec + 3f);
            yield return new WaitForSeconds(1.0f);
            Check("현장이 사라지면 결과 화면 '현장 실종'", night.Outcome.Value == NightOutcome.FieldVanished && rs.Shown && rs.TitleText == "현장 실종", rs.TitleText);
            AutoTestNet.CaptureLocal("s3_results_vanished.png");
            yield return new WaitForSeconds(0.5f);
            night.RequestBackToLobbyRpc();
            yield return new WaitForSeconds(0.8f);

            dir.ServerEvent -= OnEntityEvent;
            dir.ServerReset();
            field.ServerRestore();
            lights.ServerReviveAll();
            MimicDirector.TestForce = null;
            es.followerRuleGrace = graceWas;
            ReleaseInputs();
        }
    }
}
