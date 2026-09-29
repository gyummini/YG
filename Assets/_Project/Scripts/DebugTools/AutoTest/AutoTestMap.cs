using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using L = NightOffice.BuildingLayout;

namespace NightOffice
{
    public partial class AutoTestRunner
    {
        /// <summary>
        /// 복도식 아파트 map: mid-corridor fire door (voice + sight), west stairwell door, walking down both
        /// stairwells to 1F, and a real walk from the office door to the farthest unit.
        /// </summary>
        IEnumerator MapSuite()
        {
            var field = FieldP;
            var control = ControlP;
            const int f = 3;
            float y = L.FloorY(f);

            // ---------------------------------------------------------------- 1) 복도 중간 방화문
            var mid = Door.ByKey("fireMid" + f);
            Check("복도 방화문이 층마다 있고 도어 홀더(열린 채 유지) 설정", mid != null && mid.holdOpen && Door.ByKey("fireMid2") != null && Door.ByKey("fireMid4") != null, mid != null ? mid.label : "missing");
            Check("복도 방화문은 열린 채로 시작", mid != null && mid.IsOpen.Value, "");
            var westSpot = new Vector3(L.MidFireDoorX - 3.5f, y, L.CorridorCenterZ);
            var eastSpot = new Vector3(L.MidFireDoorX + 3.5f, y, L.CorridorCenterZ);
            control.TeleportRpc(westSpot, 90f);
            field.TeleportRpc(eastSpot, -90f);
            FieldInput(talk: true);
            yield return new WaitForSeconds(1.2f);
            Check("방화문 열림: 상황실이 듣는 현장 = 근접", Route(null) == "Proximity", Route(null));
            yield return ListenHost(2.5f);
            Check("방화문 열림: 실제 출력 레벨", m_Heard > 0.001f, $"max={m_Heard:0.0000}");
            Check("방화문 열림: 서로 보임(시야 트임)", !SightBlocked(control, field), "");
            yield return AskClient();
            Check("방화문 열림: 현장이 듣는 상황실 = 근접", m_ClientObsOk && m_ClientObs.otherRoute == "Proximity", m_ClientObs.otherRoute);

            var interact = mid.GetComponentInChildren<DoorInteract>();
            Check("열린 복도 방화문 안내: '문 닫기'", interact != null && interact.Prompt(field) == "문 닫기", interact != null ? interact.Prompt(field) : "-");

            // close it the way a player does (push from the east side, server-validated RPC from the host player)
            control.TeleportRpc(new Vector3(L.MidFireDoorX + 1.2f, y, L.CorridorCenterZ + 0.4f), -90f);
            yield return new WaitForSeconds(0.6f);
            int latchFrame = -1;
            System.Action<Door, bool> onLatch = (d, open) =>
            {
                if (d == mid && !open) latchFrame = Time.frameCount;
            };
            Door.LeafChanged += onLatch;
            mid.RequestToggleRpc();
            control.TeleportRpc(westSpot, 90f);
            float t0 = Time.time;
            while (latchFrame < 0 && Time.time - t0 < 3f) yield return null;
            Door.LeafChanged -= onLatch;
            yield return new WaitForSeconds(1.2f);
            Check("플레이어가 밀어 닫기(RPC) → 닫힘", !mid.IsOpen.Value && latchFrame > 0, $"open={mid.IsOpen.Value}");
            // the host player now stands west of the door, the field east
            float after = 0f;
            float tq = Time.time;
            while (Time.time - tq < 0.8f)
            {
                after = Mathf.Max(after, HostHearsLevel());
                yield return null;
            }
            Check("방화문 닫힘: 반대편 목소리 컷", Route(null) == "Cut", Route(null));
            Check("방화문 닫힘: 컷 이후 출력 무음", after < 0.0005f, $"max={after:0.0000}");
            Check("방화문 닫힘: 시야도 끊김", SightBlocked(control, field), "");
            yield return AskClient();
            Check("방화문 닫힘: 현장 쪽도 컷(대칭)", m_ClientObsOk && m_ClientObs.otherRoute == "Cut", m_ClientObs.otherRoute);
            Check("닫힌 복도 방화문 안내: 카드 찍기", interact != null && interact.Prompt(field) == $"카드 찍기 ({mid.label})", interact != null ? interact.Prompt(field) : "-");
            AutoTestNet.I.ClientLookRpc(-90f, 4f);
            yield return new WaitForSeconds(0.5f);
            AutoTestNet.I.ClientCaptureRpc("map_midfiredoor_closed.png");
            yield return new WaitForSeconds(0.6f);

            // other floors are unaffected
            var mid2 = Door.ByKey("fireMid2");
            Check("다른 층 복도 방화문은 그대로 열림", mid2 != null && mid2.IsOpen.Value, "");

            // open again with the card: it stays open (no auto-close) and leaves a card record
            int cards0 = CardLog.I != null ? CardLog.I.Records.Count : 0;
            control.TeleportRpc(new Vector3(L.MidFireDoorX - 1.0f, y, L.CorridorCenterZ), 90f);
            yield return new WaitForSeconds(0.6f);
            mid.RequestSwipeRpc();
            yield return new WaitForSeconds(GameSettings.I.doors.fireAutoCloseSec + 1.5f);
            control.TeleportRpc(westSpot, 90f);
            yield return new WaitForSeconds(1.0f);
            Check("카드로 다시 열면 자동으로 닫히지 않음", mid.IsOpen.Value, $"open={mid.IsOpen.Value}");
            Check("카드 기록에 복도 방화문", CardLog.I != null && CardLog.I.Records.Count == cards0 + 1, $"{cards0}->{(CardLog.I != null ? CardLog.I.Records.Count : -1)}");
            Check("다시 열림: 근접 음성", Route(null) == "Proximity", Route(null));
            FieldInput();

            // ---------------------------------------------------------------- 2) 서쪽 계단실 문턱
            var w3 = Door.ByKey("fireW" + f);
            w3.ServerClose();
            control.TeleportRpc(L.WestStair.Door(f) + L.WestStair.OutDir * 1.8f, -90f);
            field.TeleportRpc(L.WestStair.LandingCenter(f) + Vector3.left * 0.6f, 90f);
            FieldInput(talk: true);
            yield return new WaitForSeconds(1.6f);
            Check("서쪽 계단실 문 닫힘: 컷", Route(null) == "Cut", Route(null));
            Check("현장 위치 = 서쪽 계단", field.Zone != null && field.Zone.key == "stairW", field.Zone != null ? field.Zone.key : "-");
            w3.ServerOpen(0f);
            yield return new WaitForSeconds(1.0f);
            Check("서쪽 계단실 문 열림: 근접", Route(null) == "Proximity", Route(null));
            w3.ServerClose();
            yield return new WaitForSeconds(1.0f);
            Check("서쪽 계단실 문 다시 닫힘: 컷", Route(null) == "Cut", Route(null));
            FieldInput();
            control.TeleportRpc(L.OfficeSpawns[0], 90f);

            // ---------------------------------------------------------------- 3) 양 끝 계단으로 1F까지
            foreach (var s in L.Stairs)
            {
                string door1 = s.Section == L.SectionWest ? "fireW1" : "fireE1";
                field.TeleportRpc(s.LandingCenter(L.MaxFloor), 0f);
                yield return new WaitForSeconds(1.2f);
                bool ok = false;
                float secs = 0f;
                yield return WalkField(s.LandingCenter(1), 60f, (a, b) => { ok = a; secs = b; });
                var z = field.Zone;
                Check($"{s.Label}: 4F → 1F 계단참까지 걸어서 내려감", ok && field.Floor == 1 && z != null && z.key == s.Key, $"{secs:0.0}s floor={field.Floor} zone={(z != null ? z.key : "-")}");
                Door.ByKey(door1).ServerOpen(0f);
                yield return new WaitForSeconds(1.2f); // leaf swings aside, its navmesh carve moves with it
                yield return WalkField(s.Door(1) + s.OutDir * 3.0f, 20f, (a, b) => { ok = a; secs = b; });
                z = field.Zone;
                Check($"{s.Label}: 1F 방화문을 지나 로비(1층 복도)로", ok && z != null && z.key == "lobby", $"zone={(z != null ? z.key : "-")}");
                Door.ByKey(door1).ServerClose();
            }

            // ---------------------------------------------------------------- 4) 관리사무소 → 가장 먼 세대 (실제 보행)
            // closed doors carve the navmesh (entities path around them): open every stair door to measure the building
            var stairDoors = new System.Collections.Generic.List<Door>();
            for (int fl = 1; fl <= L.MaxFloor; fl++)
                foreach (var key in new[] { "fireW" + fl, "fireE" + fl })
                {
                    var d = Door.ByKey(key);
                    if (d == null) continue;
                    d.ServerOpen(0f);
                    stairDoors.Add(d);
                }
            yield return new WaitForSeconds(1.5f);
            var start = L.OfficeDoor + Vector3.right * 1.0f;
            int farUnit = 0;
            float farLen = 0f;
            foreach (var u in L.Units)
                if (PathLength(start, u.OutsideDoor, out float m) && m > farLen)
                {
                    farLen = m;
                    farUnit = u.Number;
                }
            float speed = GameSettings.I.player.walkSpeed;
            Note($"navmesh: farthest unit on foot {farUnit} = {farLen:0.0} m ({farLen / speed:0.0} s at {speed} m/s)");
            Check("가장 먼 세대까지 걸어서 약 1분(경로 길이)", farLen / speed > 45f && farLen / speed < 75f, $"{farUnit} {farLen:0.0} m / {farLen / speed:0.0} s");
            Door.ByKey("fireMid4")?.ServerOpen(0f);
            field.TeleportRpc(start, 90f);
            yield return new WaitForSeconds(1.2f);
            L.TryGetUnit(farUnit, out var target);
            bool walked = false;
            float walkSec = 0f;
            yield return WalkField(target.OutsideDoor, 120f, (a, b) => { walked = a; walkSec = b; });
            Check("실제로 걸어서 도착(방화문 미리 열어 둠)", walked, $"{walkSec:0.0}s");
            Check("실제 보행 시간 약 1분", walked && walkSec > 45f && walkSec < 80f, $"{walkSec:0.0}s to {farUnit}");
            foreach (var d in stairDoors) d.ServerClose();

            // ---------------------------------------------------------------- 5) views for the report
            yield return MapViews();
            ReleaseInputs();
        }

        IEnumerator MapViews()
        {
            var field = FieldP;
            float y = L.FloorY(3);
            FieldInput();
            // long corridor from the west stair: units on the right, open side on the left
            field.TeleportRpc(new Vector3(L.WestStairDoorX + 1.2f, y, 9.0f), 90f);
            yield return Shot(90f, 4f, "map_corridor_from_west.png");
            // over the railing: the building across with a few lit windows
            field.TeleportRpc(new Vector3(-8f, y, 9.9f), 0f);
            yield return Shot(0f, -4f, "map_open_side.png");
            // the ㄱ corner from the east part of the straight corridor
            field.TeleportRpc(new Vector3(20.5f, y, 9.4f), 90f);
            yield return Shot(96f, 3f, "map_corner.png");
            // bent corridor toward the east stair (column alcove on the right)
            field.TeleportRpc(new Vector3(L.BentCenterX, y, 8.8f), 180f);
            yield return Shot(180f, 3f, "map_bent_corridor.png");
            // elevator hall and the mid fire door
            field.TeleportRpc(new Vector3(-1.5f, y, 9.0f), 90f);
            yield return Shot(95f, 2f, "map_hall_and_middoor.png");
            // alcove: 쓰레기 투입구 and the 2F electric panel
            var chute = L.Recess(L.RecessKind.GarbageChute);
            field.TeleportRpc(new Vector3(chute.Area.center.x - 0.2f, L.FloorY(2), 9.9f), 180f);
            yield return Shot(180f, 10f, "map_alcove_chute_2F.png");
        }

        IEnumerator Shot(float yaw, float pitch, string file)
        {
            yield return new WaitForSeconds(0.8f);
            AutoTestNet.I.ClientLookRpc(yaw, pitch);
            yield return new WaitForSeconds(0.6f);
            AutoTestNet.I.ClientCaptureRpc(file);
            yield return new WaitForSeconds(0.6f);
        }

        static bool SightBlocked(PlayerNet a, PlayerNet b)
        {
            int mask = (1 << 0) | (1 << LayerMask.NameToLayer("Door"));
            return Physics.Linecast(a.HeadPosition, b.HeadPosition, mask, QueryTriggerInteraction.Ignore);
        }

        static bool PathLength(Vector3 a, Vector3 b, out float meters)
        {
            meters = 0f;
            if (!NavMesh.SamplePosition(a, out var ha, 1.0f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out var hb, 1.0f, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            for (int i = 1; i < path.corners.Length; i++)
            {
                var d = path.corners[i] - path.corners[i - 1];
                d.y = 0f;
                meters += d.magnitude;
            }
            return true;
        }

        /// <summary>
        /// Walks the field player (MPPM clone) along a navmesh path by steering its look and holding "forward",
        /// exactly like a player would; doors on the way must already be open.
        /// </summary>
        IEnumerator WalkField(Vector3 target, float timeout, System.Action<bool, float> done, bool headDown = false)
        {
            var field = FieldP;
            var path = new NavMeshPath();
            bool planned = NavMesh.SamplePosition(field.transform.position, out var ha, 1.5f, NavMesh.AllAreas)
                           && NavMesh.SamplePosition(target, out var hb, 1.5f, NavMesh.AllAreas)
                           && NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path)
                           && path.status == NavMeshPathStatus.PathComplete;
            if (!planned)
            {
                Note($"no navmesh path {field.transform.position} → {target}");
                done(false, 0f);
                yield break;
            }
            var corners = path.corners;
            float t0 = Time.time;
            int i = 1;
            float best = float.MaxValue;
            float progressAt = Time.time;
            while (i < corners.Length && Time.time - t0 < timeout)
            {
                var d = corners[i] - field.transform.position;
                float dy = d.y;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.4f && Mathf.Abs(dy) < 1.4f)
                {
                    i++;
                    best = float.MaxValue;
                    progressAt = Time.time;
                    continue;
                }
                if (dist < best - 0.15f)
                {
                    best = dist;
                    progressAt = Time.time;
                }
                if (Time.time - progressAt > 4f)
                {
                    Note($"walk stuck at {field.transform.position} → corner {i} {corners[i]}");
                    break;
                }
                AutoTestNet.I.ClientLookRpc(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
                FieldInput(move: new Vector2(0f, 1f), headDown: headDown);
                yield return new WaitForSeconds(0.06f);
            }
            FieldInput(headDown: headDown);
            yield return new WaitForSeconds(0.4f);
            done(i >= corners.Length, Time.time - t0);
        }
    }
}
