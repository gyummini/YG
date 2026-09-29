using System.Collections;
using UnityEngine;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        static readonly Vector3 OfficeA = BuildingLayout.OfficeSpawns[0];
        static readonly Vector3 OfficeB = BuildingLayout.OfficeSpawns[1];

        /// <summary>Stage 1: connection, roles, voice rules (proximity / cut / muffle / radio / relay / ducking).</summary>
        IEnumerator Stage1()
        {
            var field = FieldP;
            var control = ControlP;
            Check("역할: 호스트=상황실, 참가자=현장", control.IsOwner && !field.IsOwner, $"control={control.OwnerClientId} field={field.OwnerClientId}");

            var officeDoor = FindDoor(DoorKind.Office, 1);
            officeDoor.ServerClose();
            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            yield return new WaitForSeconds(1.5f);

            // 1) same room: proximity both ways
            yield return AskClient();
            Check("사무실 안: 상황실이 듣는 현장 = 근접", Route(null) == "Proximity", Route(null));
            Check("사무실 안: 현장이 듣는 상황실 = 근접", m_ClientObsOk && m_ClientObs.otherRoute == "Proximity", m_ClientObs.otherRoute);

            // 2) the field talks (synthetic voice): audible + ducking
            FieldInput(talk: true);
            yield return new WaitForSeconds(0.5f);
            yield return ListenHost(2.5f);
            float proxLevel = m_Heard;
            Check("근접 음성이 실제로 재생됨(출력 레벨)", proxLevel > 0.002f, $"rms={proxLevel:0.0000} {field.remoteVoice?.TestSourceState}");
            Check("덕킹: 목소리 동안 환경음 감쇠", Ducker.I.GainDb < -3f, $"{Ducker.I.GainDb:0.0}dB");

            // 3) field just outside the closed office door, control just inside it: muffled both ways
            //    (the mumble only leaks while the speaker is within muffleRadius of the door)
            field.TeleportRpc(new Vector3(1.0f, 0f, 7.0f), 90f);
            control.TeleportRpc(new Vector3(-1.5f, 0f, 6.3f), 90f);
            yield return new WaitForSeconds(1.0f);
            yield return ListenHost(2.5f);
            float muffLevel = m_Heard;
            Check("문 닫힘 + 문 바로 밖: 웅얼거림(Muffled)", Route(null) == "Muffled", Route(null));
            Check("웅얼거림도 소리는 난다", muffLevel > 0.0005f, $"rms={muffLevel:0.0000} (근접 {proxLevel:0.0000})");
            yield return AskClient();
            Check("대칭: 현장도 상황실을 웅얼거림으로", m_ClientObsOk && m_ClientObs.otherRoute == "Muffled", m_ClientObs.otherRoute);

            // 4) open the door: same space again
            officeDoor.ServerOpen(0f);
            yield return new WaitForSeconds(1.0f);
            Check("문 열림: 근접 음성으로 복귀", Route(null) == "Proximity", Route(null));

            // 5) instant cut on the latch (speaker away from the door so no mumble remains)
            field.TeleportRpc(new Vector3(5.5f, 0f, 7.0f), 90f);
            yield return new WaitForSeconds(1.2f);
            Check("문 열림 + 로비 안쪽: 근접", Route(null) == "Proximity", Route(null));
            yield return ListenHost(2.0f);
            float lvlBeforeLatch = m_Heard;
            int latchFrame = -1;
            float latchTime = 0f;
            System.Action<Door, bool> onLatch = (d, open) =>
            {
                if (d == officeDoor && !open)
                {
                    latchFrame = Time.frameCount;
                    latchTime = Time.time;
                }
            };
            Door.LeafChanged += onLatch;
            officeDoor.ServerClose();
            float t0 = Time.time;
            while (latchFrame < 0 && Time.time - t0 < 3f) yield return null;
            int cutFrame = -1;
            while (Time.time - latchTime < 0.5f)
            {
                if (cutFrame < 0 && Route(null) == "Cut") cutFrame = Time.frameCount;
                yield return null;
            }
            Door.LeafChanged -= onLatch;
            // after the cut every instant must be silent
            float lvlAfter = 0f;
            float tq = Time.time;
            while (Time.time - tq < 0.6f)
            {
                lvlAfter = Mathf.Max(lvlAfter, HostHearsLevel());
                yield return null;
            }
            Check("문이 닫히는 순간 컷(래치 후 1프레임 이내)", latchFrame > 0 && cutFrame >= 0 && cutFrame - latchFrame <= 1, $"latchFrame={latchFrame} cutFrame={cutFrame}");
            Check("컷 이후 출력 무음", lvlBeforeLatch > 0.001f && lvlAfter < 0.0005f, $"before(max 2s)={lvlBeforeLatch:0.0000} after(max 0.6s)={lvlAfter:0.0000}");

            // 6) radio dead zone around the office
            field.TeleportRpc(new Vector3(6.5f, 0f, 4.0f), 0f); // 7.2 m from the office door: inside the dead radius, outside the mumble radius
            yield return new WaitForSeconds(1.2f);
            FieldInput(ptt: true, talk: true);
            yield return new WaitForSeconds(1.2f);
            yield return AskClient();
            Check("관리사무소 주변: 무전 불통(송신 안 됨)", RadioNet.I.Transmitter.Value == RadioNet.None && m_ClientObsOk && m_ClientObs.radioDeadPress && !m_ClientObs.radioGranted,
                $"tx={RadioNet.I.Transmitter.Value} dead={m_ClientObs.radioDeadPress}");
            Check("불통 중 상황실은 아무것도 못 들음", Route(null) == "Cut", Route(null));
            FieldInput(talk: false);
            yield return new WaitForSeconds(0.5f);

            // 7) radio from 3F
            field.TeleportRpc(new Vector3(2f, BuildingLayout.FloorY(3), 9.2f), -90f);
            yield return new WaitForSeconds(1.5f);
            Check("3층 복도: 무전 없이는 컷", Route(null) == "Cut", Route(null));
            int squelch0 = RadioClient.I.SquelchCount;
            FieldInput(ptt: true, talk: true);
            yield return new WaitForSeconds(1.0f);
            Check("현장 무전 송신 점유", RadioNet.I.Transmitter.Value == field.OwnerClientId, RadioNet.I.Transmitter.Value.ToString());
            Check("상황실: 무전 경로로 들림", Route(null) == "Radio", Route(null));
            Check("상황실 수신 표시", RadioClient.I.ReceivingNow, "");
            yield return ListenHost(2.0f);
            Check("무전 음성 출력 레벨", m_Heard > 0.001f, $"max={m_Heard:0.0000} {field.remoteVoice?.TestSourceState}");
            FieldInput(ptt: false, talk: true);
            yield return new WaitForSeconds(0.12f);
            string tailRoute = Route(null);
            yield return new WaitForSeconds(0.8f);
            Check("송신 종료 후 꼬리 유지 → 컷", tailRoute == "Radio" && Route(null) == "Cut", $"tail={tailRoute} after={Route(null)}");
            Check("송신 끝에 치직(수신측)", RadioClient.I.SquelchCount == squelch0 + 1, $"{squelch0}->{RadioClient.I.SquelchCount}");
            FieldInput(talk: false);

            // 8) busy: field holds the channel, control tries
            FieldInput(ptt: true);
            yield return new WaitForSeconds(0.7f);
            int busy0 = RadioClient.I.BusyCount;
            ControlInput(ptt: true);
            yield return new WaitForSeconds(0.7f);
            Check("점유 중 누르면 송신 불가 신호음", RadioClient.I.BusyCount == busy0 + 1 && RadioClient.I.LastResult == RadioTxResult.Busy, $"busy {busy0}->{RadioClient.I.BusyCount} last={RadioClient.I.LastResult}");
            Check("점유자는 그대로 현장", RadioNet.I.Transmitter.Value == field.OwnerClientId, "");
            ControlInput(ptt: false);
            FieldInput(ptt: false);
            yield return new WaitForSeconds(0.8f);

            // 9) collision: both press at once → nobody transmits
            yield return AskClient();
            int cBusy0 = m_ClientObs.busyCount;
            busy0 = RadioClient.I.BusyCount;
            FieldInput(ptt: true);
            ControlInput(ptt: true);
            yield return new WaitForSeconds(0.9f);
            yield return AskClient();
            Check("동시에 누르면 아무것도 전달 안 됨", RadioNet.I.Transmitter.Value == RadioNet.None, RadioNet.I.Transmitter.Value.ToString());
            Check("동시 누름: 양쪽 모두 불가 신호음", RadioClient.I.BusyCount == busy0 + 1 && m_ClientObsOk && m_ClientObs.busyCount == cBusy0 + 1, $"host {busy0}->{RadioClient.I.BusyCount} client {cBusy0}->{m_ClientObs.busyCount}");
            ControlInput(ptt: false);
            FieldInput(ptt: false);
            yield return new WaitForSeconds(0.8f);

            // 10) control → field over the radio
            yield return AskClient();
            int cSquelch0 = m_ClientObs.squelchCount;
            ControlInput(ptt: true, talk: true);
            yield return new WaitForSeconds(1.2f);
            yield return AskClient();
            Check("현장: 상황실 무전이 무전 경로로", m_ClientObsOk && m_ClientObs.otherRoute == "Radio", m_ClientObs.otherRoute);
            yield return ListenClient(3);
            Check("현장 무전 출력 레벨", m_Heard > 0.001f, $"max={m_Heard:0.0000}");
            ControlInput(ptt: false, talk: false);
            yield return new WaitForSeconds(1.0f);
            yield return AskClient();
            Check("상황실 송신 끝 → 현장 쪽 치직", m_ClientObsOk && m_ClientObs.squelchCount == cSquelch0 + 1, $"{cSquelch0}->{m_ClientObs.squelchCount}");

            // 11) transmitter's surroundings relayed as network events (footsteps while walking)
            int relay0 = RadioNet.RadioRelayCount;
            FieldInput(ptt: true, move: new Vector2(0f, 1f));
            yield return new WaitForSeconds(2.5f);
            FieldInput(ptt: false);
            yield return new WaitForSeconds(0.5f);
            Check("송신 중 현장 발소리가 상황실 무전으로 재생", RadioNet.RadioRelayCount > relay0, $"relayed {RadioNet.RadioRelayCount - relay0}");
            relay0 = RadioNet.RadioRelayCount;
            FieldInput(move: new Vector2(0f, -1f));
            yield return new WaitForSeconds(1.5f);
            FieldInput();
            Check("송신 안 할 때는 전달 안 됨", RadioNet.RadioRelayCount == relay0, $"relayed {RadioNet.RadioRelayCount - relay0}");

            // 12) fire door boundary: east stairwell door on 3F (both outside the office, test only)
            var fire3 = Door.ByKey("fireE3");
            var east = BuildingLayout.EastStair;
            fire3.ServerClose();
            control.TeleportRpc(east.Door(3) + east.OutDir * 1.6f, 180f);
            field.TeleportRpc(east.LandingCenter(3) + Vector3.right * 0.8f, 0f);
            FieldInput(talk: true);
            yield return new WaitForSeconds(1.5f);
            Check("방화문 닫힘: 반대편 컷", Route(null) == "Cut", Route(null));
            fire3.ServerOpen(0f);
            yield return new WaitForSeconds(1.0f);
            Check("방화문 열림: 근접", Route(null) == "Proximity", Route(null));
            fire3.ServerClose();
            yield return new WaitForSeconds(0.8f);
            Check("방화문 다시 닫힘: 컷", Route(null) == "Cut", Route(null));

            // 13) elevator door boundary
            var el = Elevator.I;
            el.ServerResetTo(1);
            control.TeleportRpc(new Vector3(6f, 0f, 9.0f), 0f);
            field.TeleportRpc(new Vector3(6f, 0.05f, 11.7f), 180f);
            yield return new WaitForSeconds(1.5f);
            Check("엘리베이터 문 닫힘: 컷", Route(null) == "Cut", Route(null));
            el.ServerOpenDoorsNow();
            el.ServerSetHoldOpen(true);
            yield return new WaitForSeconds(1.5f);
            Check("엘리베이터 문 열림: 근접", Route(null) == "Proximity", Route(null));
            el.ServerSetHoldOpen(false);
            el.ServerCloseDoorsNow();
            yield return new WaitForSeconds(1.5f);
            Check("엘리베이터 문 닫힘: 다시 컷", Route(null) == "Cut", Route(null));

            // 14) ducking release: the hum fills back in slowly after speech
            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            FieldInput(talk: true);
            yield return new WaitForSeconds(2.5f);
            float duringDb = Ducker.I.GainDb;
            FieldInput(talk: false);
            yield return new WaitForSeconds(0.9f);
            float earlyDb = Ducker.I.GainDb;
            yield return new WaitForSeconds(3.2f);
            float lateDb = Ducker.I.GainDb;
            Check("덕킹 해제는 천천히(차오름)", duringDb < -6f && earlyDb < -2f && lateDb > -1f, $"during={duringDb:0.0} +0.9s={earlyDb:0.0} +4.1s={lateDb:0.0}");

            ReleaseInputs();

            // 15) visual checks of both instances (screenshots under TestResults/shots)
            yield return VisualChecks();
            Note($"voice service: {VoiceService.I?.StatusText}");
        }

        IEnumerator VisualChecks()
        {
            var field = FieldP;
            var control = ControlP;
            var officeDoor = FindDoor(DoorKind.Office, 1);
            officeDoor.ServerClose();
            // control looks at the office door from inside: the E prompt should read "문 열기"
            control.TeleportRpc(new Vector3(-1.4f, 0f, 7.0f), 90f);
            if (control.IsOwner) control.motor.SetLook(90f, 8f);
            yield return new WaitForSeconds(1.2f);
            var prompt = control.interactor != null ? control.interactor.Prompt : null;
            Check("상황실: 사무실 문을 보면 '문 열기' 안내", prompt == "문 열기", prompt ?? "(없음)");
            AutoTestNet.CaptureLocal("s1_control_door.png");

            // field in the 3F corridor (west end, looking down the corridor) with the flashlight on
            field.TeleportRpc(new Vector3(BuildingLayout.WestStairDoorX + 2.5f, BuildingLayout.FloorY(3), 9.0f), 90f);
            yield return new WaitForSeconds(1.0f);
            AutoTestNet.I.ClientLookRpc(90f, 8f);
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, true);
            yield return new WaitForSeconds(1.0f);
            Check("현장 손전등 켜짐이 상황실에도 동기화", field.Has(PlayerNet.Flags.FlashOn), $"flags={(PlayerNet.Flags)field.NetFlags.Value}");
            AutoTestNet.I.ClientCaptureRpc("s1_field_corridor_flashlight.png");
            yield return new WaitForSeconds(0.8f);

            // head down: view limited to the floor, synced to the host
            FieldInput(headDown: true);
            yield return new WaitForSeconds(1.0f);
            Check("현장 고개 숙이기 동기화", field.Has(PlayerNet.Flags.HeadDown) && field.Pitch.Value > 50f, $"pitch={field.Pitch.Value:0}");
            AutoTestNet.I.ClientCaptureRpc("s1_field_headdown.png");
            yield return new WaitForSeconds(0.8f);

            // eyes closed: black screen
            FieldInput(eyes: true);
            yield return new WaitForSeconds(1.0f);
            Check("현장 눈 감기 동기화", field.Has(PlayerNet.Flags.EyesClosed), "");
            AutoTestNet.I.ClientCaptureRpc("s1_field_eyes_closed.png");
            yield return new WaitForSeconds(0.8f);
            FieldInput();
            AutoTestNet.I.ClientSetFlagRpc(PlayerNet.Flags.FlashOn, false);

            // lobby view from the field with the office door and card reader
            field.TeleportRpc(new Vector3(3.2f, 0f, 7.0f), -90f);
            yield return new WaitForSeconds(1.0f);
            AutoTestNet.I.ClientLookRpc(-90f, 5f);
            yield return new WaitForSeconds(0.8f);
            AutoTestNet.I.ClientCaptureRpc("s1_field_lobby.png");
            yield return new WaitForSeconds(1.0f);
            ReleaseInputs();
        }

    }
}
