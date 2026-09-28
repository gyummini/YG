using System.Collections;
using UnityEngine;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        /// <summary>
        /// Online check (needs the Unity Cloud link + Vivox): session with Relay and a join code, Vivox channel,
        /// participant taps, and real Vivox voice through the same routes (proximity / muffle / cut / radio).
        /// The field streams the synthetic test voice into Vivox (audio injection); both microphones are silenced.
        /// </summary>
        IEnumerator Online()
        {
            var field = FieldP;
            var control = ControlP;
            yield return AskClient();
            Check("Relay 세션으로 접속(호스트)", ConnectionManager.I.UsingRelay && !string.IsNullOrEmpty(ConnectionManager.I.JoinCode), "code=" + ConnectionManager.I.JoinCode);
            Check("Relay 세션으로 접속(참가자, 같은 코드)", m_ClientObsOk && m_ClientObs.usingRelay && m_ClientObs.joinCode == ConnectionManager.I.JoinCode, "client code=" + m_ClientObs.joinCode);

            yield return WaitFor("Vivox 채널 참가(호스트)", () => VoiceService.I.State == VoiceService.VoiceState.InChannel, 40f);
            float t0 = Time.time;
            while (Time.time - t0 < 40f)
            {
                yield return AskClient();
                if (m_ClientObsOk && m_ClientObs.voiceState == "InChannel" && m_ClientObs.otherHasTap) break;
                yield return new WaitForSeconds(1f);
            }
            Check("Vivox 채널 참가(참가자)", m_ClientObsOk && m_ClientObs.voiceState == "InChannel", m_ClientObs.voiceState);
            yield return WaitFor("호스트: 상대 참가자 탭 연결", () => field.remoteVoice != null && field.remoteVoice.HasTap, 30f);
            Check("참가자: 상대 참가자 탭 연결", m_ClientObsOk && m_ClientObs.otherHasTap, $"participants={m_ClientObs.vivoxParticipants}");

            var officeDoor = FindDoor(DoorKind.Office, 1);
            officeDoor.ServerClose();
            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            yield return new WaitForSeconds(1.5f);
            AutoTestNet.I.ClientInjectRpc(true);
            yield return new WaitForSeconds(4f);
            Check("Vivox 실음성: 같은 방 근접으로 들림", Route(null) == "Proximity" && HostHearsPeak() > 0.001f, $"route={Route(null)} peak={HostHearsPeak():0.0000}");

            field.TeleportRpc(new Vector3(1.0f, 0f, 7.0f), 90f);
            yield return new WaitForSeconds(2.5f);
            Check("Vivox 실음성: 문 밖 웅얼거림", Route(null) == "Muffled" && HostHearsPeak() > 0.0005f, $"route={Route(null)} peak={HostHearsPeak():0.0000}");

            field.TeleportRpc(new Vector3(2f, BuildingLayout.FloorY(3), 9.2f), -90f);
            yield return new WaitForSeconds(2.5f);
            float cutMax = 0f;
            float tq = Time.time;
            while (Time.time - tq < 1.5f)
            {
                cutMax = Mathf.Max(cutMax, HostHearsLevel());
                yield return null;
            }
            Check("Vivox 실음성: 다른 층은 컷(무음)", Route(null) == "Cut" && cutMax < 0.0005f, $"route={Route(null)} max={cutMax:0.0000}");

            FieldInput(ptt: true);
            yield return new WaitForSeconds(2.5f);
            Check("Vivox 실음성: 무전으로 들림", Route(null) == "Radio" && HostHearsPeak() > 0.001f, $"route={Route(null)} peak={HostHearsPeak():0.0000}");
            FieldInput(ptt: false);
            yield return new WaitForSeconds(1.5f);
            AutoTestNet.I.ClientInjectRpc(false);
            control.TeleportRpc(OfficeA, 90f);
            field.TeleportRpc(OfficeB, 90f);
            ReleaseInputs();
        }
    }
}
