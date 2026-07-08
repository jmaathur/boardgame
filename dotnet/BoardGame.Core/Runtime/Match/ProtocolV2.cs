using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Hand-maintained C# mirror of core/types/src/protocol-v2.ts (the match-loop
// wire contract). Kept in sync with the zod schema; the BattleServer conformance
// tests exercise the real messages, so drift surfaces there.
namespace BoardGame.Core.Match
{
    public static class ProtocolV2
    {
        public const int Version = 2;
    }

    // -----------------------------------------------------------------------
    // Blueprint / state DTOs (member layout is a pure function of the catalog)
    // -----------------------------------------------------------------------

    public sealed class WireAnchor
    {
        [JsonProperty("row")] public int Row { get; set; }
        [JsonProperty("col")] public int Col { get; set; }
    }

    public sealed class SquadCardDto
    {
        [JsonProperty("cardId")] public string CardId { get; set; } = "";
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("anchor")] public WireAnchor Anchor { get; set; } = new WireAnchor();
        [JsonProperty("orientation")] public string Orientation { get; set; } = "north";
        [JsonProperty("level")] public int Level { get; set; } = 1;
        [JsonProperty("xp")] public double Xp { get; set; }
        [JsonProperty("purchasedRound")] public int PurchasedRound { get; set; }
        [JsonProperty("invested")] public int Invested { get; set; }
    }

    public sealed class SeatTechStateDto
    {
        [JsonProperty("unlockedUnits")] public List<string> UnlockedUnits { get; set; } = new List<string>();
        [JsonProperty("purchasedTechs")] public List<string> PurchasedTechs { get; set; } = new List<string>();
        [JsonProperty("techPriceByUnit")] public Dictionary<string, int> TechPriceByUnit { get; set; } = new Dictionary<string, int>();
    }

    public sealed class SeatViewDto
    {
        [JsonProperty("seat")] public int Seat { get; set; }
        [JsonProperty("playerName")] public string PlayerName { get; set; } = "";
        [JsonProperty("connected")] public bool Connected { get; set; }
        [JsonProperty("commanderId")] public string? CommanderId { get; set; }
        [JsonProperty("hp")] public int Hp { get; set; }
        [JsonProperty("coin")] public int Coin { get; set; }
        [JsonProperty("deploysRemaining")] public int DeploysRemaining { get; set; }
        [JsonProperty("unlocksRemaining")] public int UnlocksRemaining { get; set; }
        [JsonProperty("ready")] public bool Ready { get; set; }
        [JsonProperty("cards")] public List<SquadCardDto> Cards { get; set; } = new List<SquadCardDto>();
        [JsonProperty("tech")] public SeatTechStateDto Tech { get; set; } = new SeatTechStateDto();
    }

    public sealed class OpponentViewDto
    {
        [JsonProperty("seat")] public int Seat { get; set; }
        [JsonProperty("playerName")] public string PlayerName { get; set; } = "";
        [JsonProperty("connected")] public bool Connected { get; set; }
        [JsonProperty("commanderId")] public string? CommanderId { get; set; }
        [JsonProperty("hp")] public int Hp { get; set; }
        [JsonProperty("cards")] public List<SquadCardDto> Cards { get; set; } = new List<SquadCardDto>();
    }

    public sealed class MatchConfigDto
    {
        [JsonProperty("board")] public BoardWH Board { get; set; } = new BoardWH();
        [JsonProperty("deploysPerRound")] public int DeploysPerRound { get; set; }
        [JsonProperty("unlocksPerRound")] public int UnlocksPerRound { get; set; }
        [JsonProperty("incomePerRoundIncrement")] public int IncomePerRoundIncrement { get; set; }
        [JsonProperty("deploySeconds")] public int DeploySeconds { get; set; }
        [JsonProperty("battleSeconds")] public int BattleSeconds { get; set; }
        [JsonProperty("commanderPickSeconds")] public int CommanderPickSeconds { get; set; }
        [JsonProperty("commandersOffered")] public int CommandersOffered { get; set; }
    }

    public sealed class BoardWH
    {
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }
    }

    public sealed class MatchSnapshotDto
    {
        [JsonProperty("phase")] public string Phase { get; set; } = "lobby";
        [JsonProperty("round")] public int Round { get; set; }
        [JsonProperty("phaseDeadline")] public long PhaseDeadline { get; set; }
        [JsonProperty("commanderOffers")] public List<string> CommanderOffers { get; set; } = new List<string>();
        [JsonProperty("own")] public SeatViewDto Own { get; set; } = new SeatViewDto();
        [JsonProperty("opponent")] public OpponentViewDto? Opponent { get; set; }
    }

    // -----------------------------------------------------------------------
    // Client → server (discriminated on "type")
    // -----------------------------------------------------------------------

    public abstract class ClientMessageV2
    {
        [JsonProperty("type")] public abstract string Type { get; }

        public static ClientMessageV2? Parse(string json)
        {
            var o = JObject.Parse(json);
            var type = (string?)o["type"];
            ClientMessageV2? msg = type switch
            {
                "join" => new JoinV2(),
                "pickCommander" => new PickCommanderMsg(),
                "buySquad" => new BuySquadMsg(),
                "moveSquad" => new MoveSquadMsg(),
                "sellSquad" => new SellSquadMsg(),
                "unlockUnit" => new UnlockUnitMsg(),
                "buyTech" => new BuyTechMsg(),
                "buyLevel" => new BuyLevelMsg(),
                "setReady" => new SetReadyMsg(),
                "battleAck" => new BattleAckMsg(),
                "ping" => new PingV2Msg(),
                _ => null,
            };
            if (msg == null) return null;
            using var reader = o.CreateReader();
            JsonSerializer.CreateDefault().Populate(reader, msg);
            return msg;
        }
    }

    public sealed class JoinV2 : ClientMessageV2
    {
        public override string Type => "join";
        [JsonProperty("roomId")] public string RoomId { get; set; } = "";
        [JsonProperty("playerName")] public string PlayerName { get; set; } = "";
        [JsonProperty("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonProperty("resumeToken")] public string? ResumeToken { get; set; }
        [JsonProperty("catalogHash")] public string? CatalogHash { get; set; }
    }

    public sealed class PickCommanderMsg : ClientMessageV2
    {
        public override string Type => "pickCommander";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("commanderId")] public string CommanderId { get; set; } = "";
    }

    public sealed class BuySquadMsg : ClientMessageV2
    {
        public override string Type => "buySquad";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("anchor")] public WireAnchor Anchor { get; set; } = new WireAnchor();
        [JsonProperty("orientation")] public string Orientation { get; set; } = "north";
    }

    public sealed class MoveSquadMsg : ClientMessageV2
    {
        public override string Type => "moveSquad";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("cardId")] public string CardId { get; set; } = "";
        [JsonProperty("anchor")] public WireAnchor Anchor { get; set; } = new WireAnchor();
        [JsonProperty("orientation")] public string Orientation { get; set; } = "north";
    }

    public sealed class SellSquadMsg : ClientMessageV2
    {
        public override string Type => "sellSquad";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("cardId")] public string CardId { get; set; } = "";
    }

    public sealed class UnlockUnitMsg : ClientMessageV2
    {
        public override string Type => "unlockUnit";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
    }

    public sealed class BuyTechMsg : ClientMessageV2
    {
        public override string Type => "buyTech";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("techId")] public string TechId { get; set; } = "";
    }

    public sealed class BuyLevelMsg : ClientMessageV2
    {
        public override string Type => "buyLevel";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("cardId")] public string CardId { get; set; } = "";
    }

    public sealed class SetReadyMsg : ClientMessageV2
    {
        public override string Type => "setReady";
        [JsonProperty("cmdId")] public string CmdId { get; set; } = "";
        [JsonProperty("ready")] public bool Ready { get; set; }
    }

    public sealed class BattleAckMsg : ClientMessageV2 { public override string Type => "battleAck"; }
    public sealed class PingV2Msg : ClientMessageV2 { public override string Type => "ping"; }

    // -----------------------------------------------------------------------
    // Server → client (plain serializable objects; discriminated by "type")
    // -----------------------------------------------------------------------

    public static class ServerMsg
    {
        public static object Welcome(int seat, string resumeToken, string catalogJson, string catalogHash, MatchConfigDto cfg, MatchSnapshotDto? snapshot)
            => new { type = "welcome", seat, resumeToken, catalogJson, catalogHash, matchConfig = cfg, match = snapshot };

        public static object Phase(MatchSnapshotDto snapshot) => new { type = "phase", match = snapshot };
        public static object CmdAccepted(string cmdId, MatchSnapshotDto snapshot) => new { type = "cmdAccepted", cmdId, match = snapshot };
        public static object CmdRejected(string cmdId, string code, string message) => new { type = "cmdRejected", cmdId, code, message };
        public static object Error(string code, string message) => new { type = "error", code, message };
        public static object Pong() => new { type = "pong" };

        public static object RevealSnapshot(int round, IEnumerable<object> armies) => new { type = "revealSnapshot", round, armies };
        public static object BattleStarted(int round, long startAtServerMs, bool hasBattleLog) => new { type = "battleStarted", round, startAtServerMs, hasBattleLog };
        public static object BattleLog(int round, object log) => new { type = "battleLog", round, log };
        public static object RoundResult(int round, int? winnerSeat, IEnumerable<object> hpDamage, IEnumerable<object> hp, string summary)
            => new { type = "roundResult", round, winnerSeat, hpDamage, hp, summary };
        public static object MatchEnded(int? winnerSeat, IEnumerable<object> finalHp) => new { type = "matchEnded", winnerSeat, finalHp };
    }
}
