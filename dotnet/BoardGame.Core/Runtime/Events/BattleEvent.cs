using System.Collections.Generic;
using Newtonsoft.Json;

namespace BoardGame.Core.Events
{
    /// <summary>
    /// Battle event log — the pre-simulated record clients play back (design
    /// doc §7). Events are emitted in tick order; the whole log is shipped once
    /// per round. Kept compact and JSON-serializable (Newtonsoft) for the wire.
    ///
    /// Events split into "state" (spawns, deaths, ownership) and "transient"
    /// (fires, damage, keyframes) so a viewer can SeekTo. This v1 set covers the
    /// families the base pack exercises; more are added as content needs them.
    /// </summary>
    public abstract class BattleEvent
    {
        [JsonProperty("t")] public int Tick { get; set; }
        [JsonProperty("e")] public abstract string EventType { get; }
    }

    public sealed class BattleStartedEvent : BattleEvent
    {
        public override string EventType => "battleStarted";
        [JsonProperty("seed")] public uint Seed { get; set; }
        [JsonProperty("tickRate")] public int TickRate { get; set; }
        [JsonProperty("durationTicks")] public int DurationTicks { get; set; }
    }

    public sealed class SquadSpawnedEvent : BattleEvent
    {
        public override string EventType => "squadSpawned";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("seat")] public int Seat { get; set; }
        [JsonProperty("unit")] public string UnitId { get; set; } = "";
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("members")] public List<MemberSpawn> Members { get; set; } = new List<MemberSpawn>();
    }

    public sealed class MemberSpawn
    {
        [JsonProperty("i")] public int Index { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("hp")] public double Hp { get; set; }
    }

    /// <summary>Moved-only position keyframes for a squad's members (5 Hz).</summary>
    public sealed class PositionKeyframesEvent : BattleEvent
    {
        public override string EventType => "pos";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("m")] public List<MemberPos> Positions { get; set; } = new List<MemberPos>();
    }

    public sealed class MemberPos
    {
        [JsonProperty("i")] public int Index { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
    }

    public sealed class AttackFiredEvent : BattleEvent
    {
        public override string EventType => "fire";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("i")] public int MemberIndex { get; set; }
        [JsonProperty("w")] public string WeaponId { get; set; } = "";
        [JsonProperty("tsq")] public int TargetSquadId { get; set; }
        [JsonProperty("ti")] public int TargetMemberIndex { get; set; }
    }

    public sealed class DamageAppliedEvent : BattleEvent
    {
        public override string EventType => "dmg";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("i")] public int MemberIndex { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; } = "hull"; // hull|shield
        [JsonProperty("amt")] public double Amount { get; set; }
        [JsonProperty("hpAfter")] public double HpAfter { get; set; }
    }

    public sealed class MemberDiedEvent : BattleEvent
    {
        public override string EventType => "died";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("i")] public int MemberIndex { get; set; }
    }

    public sealed class StatusAppliedEvent : BattleEvent
    {
        public override string EventType => "status+";
        [JsonProperty("sq")] public int BattleSquadId { get; set; }
        [JsonProperty("i")] public int MemberIndex { get; set; }
        [JsonProperty("status")] public string StatusId { get; set; } = "";
    }

    public sealed class BattleEndedEvent : BattleEvent
    {
        public override string EventType => "battleEnded";
        [JsonProperty("winnerSeat")] public int WinnerSeat { get; set; } // -1 = draw
        [JsonProperty("reason")] public string Reason { get; set; } = "";
    }

    /// <summary>Collects battle events. A null sink discards (headless balance).</summary>
    public interface IBattleEventSink
    {
        void Emit(BattleEvent e);
    }

    public sealed class ListEventSink : IBattleEventSink
    {
        public List<BattleEvent> Events { get; } = new List<BattleEvent>();
        public void Emit(BattleEvent e) => Events.Add(e);
    }

    public sealed class NullEventSink : IBattleEventSink
    {
        public static readonly NullEventSink Instance = new NullEventSink();
        public void Emit(BattleEvent e) { }
    }
}
