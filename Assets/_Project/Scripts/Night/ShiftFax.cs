using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace NightOffice
{
    /// <summary>
    /// 교대 팩스: at 00:00 the fax prints the basic rules and tonight's three single-use knock codes. Both players can
    /// read it at the fax machine (the field needs the codes to knock on the way back).
    /// </summary>
    public class ShiftFax : NetSingleton<ShiftFax>
    {
        /// <summary>기본 규칙 (00:00 팩스), from the plan.</summary>
        public static readonly string[] BasicRules =
        {
            "관리사무소 문은 노크 암호와 카드 기록을 확인한 뒤 안에서만 연다.",
            "무전은 호출 부호로 시작한다. 호출 부호가 없거나 틀린 부름에는 응답하지 않는다.",
            "손전등을 누구의 얼굴에도 비추지 않는다.",
            "노크 암호는 한 번 쓰면 폐기한다. 흉내쟁이는 들은 노크를 따라 한다.",
        };

        public const string CallSignControl = "본부";
        public const string CallSignField = "순찰";

        /// <summary>"2-1|1-3|3-1-2" (groups of knocks separated by pauses).</summary>
        public readonly NetworkVariable<FixedString64Bytes> Codes = new NetworkVariable<FixedString64Bytes>(default);
        public readonly NetworkVariable<int> PrintCount = new NetworkVariable<int>(0);
        public readonly NetworkVariable<float> PrintedAtMinute = new NetworkVariable<float>(0f);

        public string[] CodeList
        {
            get
            {
                var s = Codes.Value.ToString();
                return string.IsNullOrEmpty(s) ? new string[0] : s.Split('|');
            }
        }

        public bool Printed => PrintCount.Value > 0;

        public void ServerPrint(System.Random rng)
        {
            if (!IsServer) return;
            var codes = Generate(rng, Mathf.Max(1, GameSettings.I.night.knockCodeCount));
            Codes.Value = new FixedString64Bytes(string.Join("|", codes));
            PrintedAtMinute.Value = GameClock.MinutesNow;
            PrintCount.Value++;
            PlayPrintRpc();
            GameLog.Info("Fax", "교대 팩스: 노크 암호 " + string.Join(", ", codes));
        }

        /// <summary>Unique codes of 2~3 groups, 1~3 knocks per group, never the same group twice in a row.</summary>
        public static List<string> Generate(System.Random rng, int count)
        {
            var result = new List<string>();
            int guard = 0;
            while (result.Count < count && guard++ < 500)
            {
                int groups = rng.Next(2, 4);
                var parts = new List<int>();
                for (int g = 0; g < groups; g++)
                {
                    int n;
                    do n = rng.Next(1, 4);
                    while (parts.Count > 0 && parts[parts.Count - 1] == n);
                    parts.Add(n);
                }
                var code = string.Join("-", parts);
                if (!result.Contains(code)) result.Add(code);
            }
            return result;
        }

        /// <summary>"2-1" → "●● ●" (how it sounds).</summary>
        public static string Dots(string code)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in code.Split('-'))
            {
                if (sb.Length > 0) sb.Append("  ");
                if (int.TryParse(part, out int n)) sb.Append(new string('●', n));
            }
            return sb.ToString();
        }

        [Rpc(SendTo.Everyone)]
        void PlayPrintRpc()
        {
            AudioService.I?.PlayAt(SfxId.FaxPrint, BuildingLayout.FaxMachine, 1f);
        }
    }
}
