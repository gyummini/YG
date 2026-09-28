using System;
using Unity.Collections;
using Unity.Netcode;

namespace NightOffice
{
    public struct CardRecord : INetworkSerializable, IEquatable<CardRecord>
    {
        public short Minute; // game minutes since 00:00
        public FixedString64Bytes Label;
        public bool Ok;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Minute);
            serializer.SerializeValue(ref Label);
            serializer.SerializeValue(ref Ok);
        }

        public bool Equals(CardRecord other) => Minute == other.Minute && Label.Equals(other.Label) && Ok == other.Ok;
    }

    /// <summary>출입 카드 기록: every swipe of the field card (time, place, accepted/denied).</summary>
    public class CardLog : NetSingleton<CardLog>
    {
        public NetworkList<CardRecord> Records;

        protected override void Awake()
        {
            base.Awake();
            Records = new NetworkList<CardRecord>();
        }

        public void ServerAdd(string label, bool ok)
        {
            if (!IsServer) return;
            var rec = new CardRecord { Minute = (short)GameClock.MinutesNow, Label = new FixedString64Bytes(label), Ok = ok };
            Records.Add(rec);
            while (Records.Count > 40) Records.RemoveAt(0);
            GameLog.Info("Card", $"{GameClock.Format(rec.Minute)} {label} {(ok ? "승인" : "거부")}");
        }

        public void ServerClear()
        {
            if (IsServer) Records.Clear();
        }

        public bool TryLast(out CardRecord rec)
        {
            rec = default;
            if (Records == null || Records.Count == 0) return false;
            rec = Records[Records.Count - 1];
            return true;
        }
    }
}
