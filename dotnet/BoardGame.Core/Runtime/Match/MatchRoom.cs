using System;
using System.Collections.Generic;
using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Match
{
    public enum Phase { Lobby, CommanderPick, Planning, Battle, Results, MatchEnded }

    public readonly struct CommandOutcome
    {
        public readonly bool Ok;
        public readonly string Code;
        public readonly string Message;
        private CommandOutcome(bool ok, string code, string message) { Ok = ok; Code = code; Message = message; }
        public static readonly CommandOutcome Success = new CommandOutcome(true, "", "");
        public static CommandOutcome Fail(string code, string message) => new CommandOutcome(false, code, message);
    }

    /// <summary>
    /// C# port of apps/game-server/src/matchRoom.ts — the pure, clock-injected
    /// match reducer. Structurally identical to the TS spec so the ported test
    /// suite confirms equivalence. At plan-lock the BattleServer runs the Core
    /// sim and ships a real battleLog (the only behavioral difference from the
    /// Bun stub). No wall-clock or RNG here — deterministic and restart-safe.
    /// </summary>
    public sealed class MatchRoom
    {
        public const double SurvivorFactor = 0.5;
        public const int MinRoundDamage = 100;

        private static readonly int[] Seats = { 0, 1 };

        public string Id { get; }
        private readonly LoadedCatalog _catalog;
        private readonly MatchRules _rules;

        public Phase CurrentPhase { get; private set; } = Phase.Lobby;
        public int CurrentRound { get; private set; }
        public long PhaseDeadline { get; private set; }
        public int? Winner { get; private set; }

        private int _nextCardId = 1;
        private readonly SeatState[] _seats;
        private PendingBattle? _pending;
        private RoundResultData? _lastResult;

        public MatchRoom(string id, LoadedCatalog catalog, Func<int, string> tokenGen)
        {
            Id = id;
            _catalog = catalog;
            _rules = catalog.MatchRules;
            _seats = Seats.Select(seat => new SeatState { Seat = seat, ResumeToken = tokenGen(seat) }).ToArray();
        }

        public bool IsEmpty => _seats.All(s => !s.Connected);
        private bool BothConnected => _seats.All(s => s.Connected);

        // -------------------------------------------------------------------
        // Persistence: capture / restore the full reconstructable state so a
        // server restart resumes the match (design doc §8). Connection-only
        // fields (PlayerId/Connected) are NOT persisted — players reconnect via
        // their resumeToken. Mid-battle: on restore the phase is preserved; the
        // battle log is recomputed deterministically at the next plan-lock if
        // needed (seed is round-derived).
        // -------------------------------------------------------------------

        public MatchRoomSnapshot CaptureState() => new MatchRoomSnapshot
        {
            Id = Id,
            Phase = PhaseToString(CurrentPhase),
            Round = CurrentRound,
            PhaseDeadline = PhaseDeadline,
            NextCardId = _nextCardId,
            Winner = Winner,
            Seats = _seats.Select(s => new MatchRoomSnapshot.SeatSnapshot
            {
                Seat = s.Seat,
                PlayerName = s.PlayerName,
                ResumeToken = s.ResumeToken,
                CommanderId = s.CommanderId,
                CommanderOffers = s.CommanderOffers.ToList(),
                Hp = s.Hp,
                Coin = s.Coin,
                DeploysRemaining = s.DeploysRemaining,
                UnlocksRemaining = s.UnlocksRemaining,
                Ready = s.Ready,
                Cards = s.Cards.Select(CloneCard).ToList(),
                RevealedCards = s.RevealedCards.Select(CloneCard).ToList(),
                UnlockedUnits = s.UnlockedUnits.ToList(),
                PurchasedTechs = s.PurchasedTechs.ToList(),
                TechPriceByUnit = new Dictionary<string, int>(s.TechPriceByUnit),
            }).ToList(),
        };

        public void RestoreState(MatchRoomSnapshot snap)
        {
            CurrentPhase = ParsePhase(snap.Phase);
            CurrentRound = snap.Round;
            PhaseDeadline = snap.PhaseDeadline;
            _nextCardId = snap.NextCardId;
            Winner = snap.Winner;
            foreach (var ss in snap.Seats)
            {
                var s = _seats[ss.Seat];
                s.PlayerName = ss.PlayerName;
                s.ResumeToken = ss.ResumeToken;
                s.CommanderId = ss.CommanderId;
                s.CommanderOffers = ss.CommanderOffers.ToList();
                s.Hp = ss.Hp;
                s.Coin = ss.Coin;
                s.DeploysRemaining = ss.DeploysRemaining;
                s.UnlocksRemaining = ss.UnlocksRemaining;
                s.Ready = ss.Ready;
                s.Cards = ss.Cards.Select(CloneCard).ToList();
                s.RevealedCards = ss.RevealedCards.Select(CloneCard).ToList();
                s.UnlockedUnits = new HashSet<string>(ss.UnlockedUnits);
                s.PurchasedTechs = new HashSet<string>(ss.PurchasedTechs);
                s.TechPriceByUnit = new Dictionary<string, int>(ss.TechPriceByUnit);
                s.PlayerId = null;
                s.Connected = false;
            }
        }

        private static Phase ParsePhase(string p) => p switch
        {
            "commanderPick" => Phase.CommanderPick,
            "planning" => Phase.Planning,
            "battle" => Phase.Battle,
            "results" => Phase.Results,
            "matchEnded" => Phase.MatchEnded,
            _ => Phase.Lobby,
        };

        // -------------------------------------------------------------------
        // Membership
        // -------------------------------------------------------------------

        public int? SeatByResumeToken(string token)
        {
            var s = _seats.FirstOrDefault(x => x.ResumeToken == token);
            return s?.Seat;
        }

        public (int seat, string resumeToken)? Join(string playerId, string playerName, long now, string? resumeToken = null)
        {
            if (resumeToken != null)
            {
                var seatNo = SeatByResumeToken(resumeToken);
                if (seatNo != null)
                {
                    var s = _seats[seatNo.Value];
                    s.PlayerId = playerId;
                    if (!string.IsNullOrEmpty(playerName)) s.PlayerName = playerName;
                    s.Connected = true;
                    return (s.Seat, s.ResumeToken);
                }
            }
            var free = _seats.FirstOrDefault(x => !x.Connected && x.PlayerId == null);
            if (free == null) return null;
            free.PlayerId = playerId;
            free.PlayerName = playerName;
            free.Connected = true;
            MaybeStartCommanderPick(now);
            return (free.Seat, free.ResumeToken);
        }

        public void Disconnect(string playerId)
        {
            var s = _seats.FirstOrDefault(x => x.PlayerId == playerId);
            if (s != null) s.Connected = false;
        }

        public int? SeatOfPlayer(string playerId)
        {
            var s = _seats.FirstOrDefault(x => x.PlayerId == playerId);
            return s?.Seat;
        }

        // -------------------------------------------------------------------
        // Phase transitions
        // -------------------------------------------------------------------

        private void MaybeStartCommanderPick(long now)
        {
            if (CurrentPhase != Phase.Lobby || !BothConnected) return;
            CurrentPhase = Phase.CommanderPick;
            int offered = _rules.CommandersOffered;
            var all = _rules.Commanders.Select(c => c.Id).ToList();
            foreach (var s in _seats)
            {
                s.CommanderOffers = Rotate(all, s.Seat).Take(offered).ToList();
                s.CommanderId = null;
            }
            PhaseDeadline = now + (long)_rules.Timers.CommanderPickSeconds * 1000;
        }

        /// <summary>Advance deadlines. Returns true if a transition occurred.</summary>
        public bool Tick(long now)
        {
            if (PhaseDeadline != 0 && now < PhaseDeadline) return false;
            switch (CurrentPhase)
            {
                case Phase.CommanderPick:
                    foreach (var s in _seats)
                        if (s.CommanderId == null) s.CommanderId = s.CommanderOffers.FirstOrDefault();
                    BeginPlanning(1, now);
                    return true;
                case Phase.Planning:
                    PlanLock(now);
                    return true;
                case Phase.Battle:
                    ResolveBattle(now);
                    return true;
                case Phase.Results:
                    AfterResults(now);
                    return true;
                default:
                    return false;
            }
        }

        private bool AllCommandersPicked() => _seats.All(s => s.CommanderId != null);

        private void BeginPlanning(int round, long now)
        {
            CurrentPhase = Phase.Planning;
            CurrentRound = round;
            foreach (var s in _seats)
            {
                if (round == 1) MaterializeStartingArmy(s);
                s.Coin += IncomeForSeat(s, round);
                s.DeploysRemaining = DeploysForSeat(s);
                s.UnlocksRemaining = UnlocksForSeat(s);
                s.Ready = false;
            }
            PhaseDeadline = now + (long)_rules.Timers.DeploySeconds * 1000;
        }

        private void PlanLock(long now)
        {
            foreach (var s in _seats) s.RevealedCards = s.Cards.Select(CloneCard).ToList();
            _pending = new PendingBattle
            {
                Armies = _seats.Select(s => (s.Seat, s.RevealedCards.Select(CloneCard).ToList())).ToList(),
                Acked = new HashSet<int>(),
            };
            CurrentPhase = Phase.Battle;
            PhaseDeadline = now + (long)_rules.Timers.BattleSeconds * 1000;
            RunBattle();
        }

        public void BattleAck(string playerId, long now)
        {
            if (CurrentPhase != Phase.Battle || _pending == null) return;
            var seat = SeatOfPlayer(playerId);
            if (seat == null) return;
            _pending.Acked.Add(seat.Value);
            if (_pending.Acked.Count == Seats.Length) ResolveBattle(now);
        }

        /// <summary>The battle event log from the last plan-lock (M5: real sim).</summary>
        public object? LastBattleLog { get; private set; }
        public int LastBattleRound { get; private set; }

        private void RunBattle()
        {
            // Run the Core sim on the revealed armies and keep its event log +
            // survivor values. This is the one behavioral change from the Bun stub.
            if (_pending == null) return;
            var sink = new Events.ListEventSink();
            var simA = ToArmy(0);
            var simB = ToArmy(1);
            var result = new Sim.BattleSim(_catalog, (uint)(1000 + CurrentRound), sink).Run(simA, simB);
            _pending.SimResult = result;
            LastBattleLog = sink.Events;
            LastBattleRound = CurrentRound;
        }

        private Sim.ArmyBlueprint ToArmy(int seat)
        {
            var army = new Sim.ArmyBlueprint { Seat = seat };
            var s = _seats[seat];
            foreach (var c in s.RevealedCards)
            {
                if (!_catalog.HasUnit(c.UnitId)) continue;
                army.Squads.Add(new Sim.SquadBlueprint
                {
                    UnitId = c.UnitId,
                    AnchorRow = c.Anchor.Row,
                    AnchorCol = c.Anchor.Col,
                    Orientation = ParseOrientation(c.Orientation),
                    Level = c.Level,
                    Invested = c.Invested,
                    CardId = c.CardId,
                });
            }
            return army;
        }

        private void ResolveBattle(long now)
        {
            if (CurrentPhase != Phase.Battle) return;

            // Prefer the real sim's prorated survivor values; fall back to invested
            // value if the sim produced nothing (empty armies).
            int a, b;
            int? winnerSeat;
            if (_pending?.SimResult != null)
            {
                var r = _pending.SimResult;
                a = r.SurvivorValueBySeat.GetValueOrDefault(0);
                b = r.SurvivorValueBySeat.GetValueOrDefault(1);
                winnerSeat = r.WinnerSeat < 0 ? (int?)null : r.WinnerSeat;
            }
            else
            {
                a = _seats[0].RevealedCards.Sum(c => c.Invested);
                b = _seats[1].RevealedCards.Sum(c => c.Invested);
                winnerSeat = a == b ? (int?)null : (a > b ? 0 : 1);
            }

            int damage;
            if (winnerSeat == null)
            {
                damage = (int)Math.Round(Math.Min(a, b) * SurvivorFactor);
            }
            else
            {
                // Survivor value the winner inflicts (already prorated by the sim).
                int winnerValue = winnerSeat == 0 ? a : b;
                damage = Math.Max(MinRoundDamage, winnerValue);
            }

            var hpDamage = new List<(int seat, int amount)>();
            foreach (var s in _seats)
            {
                bool takes = winnerSeat == null || s.Seat != winnerSeat.Value;
                int amount = takes ? damage : 0;
                if (amount > 0) s.Hp = Math.Max(0, s.Hp - amount);
                hpDamage.Add((s.Seat, amount));
            }

            _lastResult = new RoundResultData { Round = CurrentRound, WinnerSeat = winnerSeat, HpDamage = hpDamage };
            _pending = null;
            CurrentPhase = Phase.Results;
            PhaseDeadline = now + (long)_rules.Timers.ResultsHoldSeconds * 1000;
        }

        private void AfterResults(long now)
        {
            if (CurrentPhase != Phase.Results) return;
            var dead = _seats.Where(s => s.Hp <= 0).ToList();
            if (dead.Count > 0)
            {
                var alive = _seats.Where(s => s.Hp > 0).ToList();
                Winner = alive.Count == 1 ? alive[0].Seat : (int?)null;
                CurrentPhase = Phase.MatchEnded;
                PhaseDeadline = 0;
                return;
            }
            BeginPlanning(CurrentRound + 1, now);
        }

        // -------------------------------------------------------------------
        // Economy
        // -------------------------------------------------------------------

        private int IncomeForSeat(SeatState s, int round)
        {
            int income = _rules.Income.PerRoundIncrement * round;
            if (round == 1) income += _rules.Income.StartingIncome;
            income += CommanderEconomy(s).income;
            return income;
        }

        private int DeploysForSeat(SeatState s) => _rules.DeploysPerRound + CommanderEconomy(s).deploy;
        private int UnlocksForSeat(SeatState s) => _rules.UnlocksPerRound + CommanderEconomy(s).unlock;

        private (int income, int deploy, int unlock, int startIncome) CommanderEconomy(SeatState s)
        {
            int income = 0, deploy = 0, unlock = 0, start = 0;
            if (s.CommanderId == null) return (0, 0, 0, 0);
            var cmd = _rules.Commanders.FirstOrDefault(c => c.Id == s.CommanderId);
            if (cmd == null) return (0, 0, 0, 0);
            foreach (var ability in cmd.Ability)
            {
                if (ability is CommanderEconomyMod e)
                {
                    income += e.IncomePerRoundAdd;
                    deploy += e.DeploySlotsAdd;
                    unlock += e.UnlockSlotsAdd;
                    start += e.StartingIncomeAdd;
                }
            }
            return (income, deploy, unlock, start);
        }

        private void MaterializeStartingArmy(SeatState s)
        {
            var cmd = _rules.Commanders.FirstOrDefault(c => c.Id == s.CommanderId);
            s.Hp = cmd?.Hp ?? 5000;
            s.Coin = CommanderEconomy(s).startIncome;
            foreach (var b in _rules.StartingBuildings)
            {
                if (b.Seat != s.Seat) continue;
                AddCard(s, b.UnitId, b.Anchor.Row, b.Anchor.Col, OrientationToString(b.Orientation), 0, free: true);
            }
            foreach (var su in cmd?.StartingUnits ?? new List<PlacedUnit>())
                AddCard(s, su.UnitId, su.Anchor.Row, su.Anchor.Col, OrientationToString(su.Orientation), 0, free: true);
        }

        // -------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------

        public CommandOutcome PickCommander(string playerId, string commanderId, long now)
        {
            var s = RequireSeat(playerId);
            if (s == null) return CommandOutcome.Fail("notJoined", "not in this room");
            if (CurrentPhase != Phase.CommanderPick) return CommandOutcome.Fail("wrongPhase", "not in commander pick");
            if (!s.CommanderOffers.Contains(commanderId)) return CommandOutcome.Fail("unknownCommander", "not offered");
            if (s.CommanderId != null) return CommandOutcome.Fail("commanderAlreadyPicked", "already picked");
            s.CommanderId = commanderId;
            if (AllCommandersPicked()) BeginPlanning(1, now);
            return CommandOutcome.Success;
        }

        public CommandOutcome BuySquad(string playerId, string unitId, int row, int col, string orientation)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            if (!_catalog.TryGetUnit(unitId, out var unit)) return CommandOutcome.Fail("unknownUnit", "no unit");
            if (unit.Cost.UnlockCost > 0 && !s.UnlockedUnits.Contains(unitId)) return CommandOutcome.Fail("notUnlocked", "not unlocked");
            if (s.DeploysRemaining <= 0) return CommandOutcome.Fail("noDeploysLeft", "no deploys");
            if (s.Coin < unit.Cost.DeployCost) return CommandOutcome.Fail("insufficientFunds", "not enough coin");
            var placement = ValidatePlacement(s, unit, row, col, null);
            if (placement != null) return placement.Value;
            s.Coin -= unit.Cost.DeployCost;
            s.DeploysRemaining -= 1;
            AddCard(s, unitId, row, col, orientation, unit.Cost.DeployCost, free: false);
            return CommandOutcome.Success;
        }

        public CommandOutcome MoveSquad(string playerId, string cardId, int row, int col, string orientation)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            var card = s.Cards.FirstOrDefault(c => c.CardId == cardId);
            if (card == null) return CommandOutcome.Fail("unknownCard", "no card");
            if (!_catalog.TryGetUnit(card.UnitId, out var unit)) return CommandOutcome.Fail("unknownUnit", "unit missing");
            var placement = ValidatePlacement(s, unit, row, col, cardId);
            if (placement != null) return placement.Value;
            card.Anchor = new SquadCardData.AnchorData { Row = row, Col = col };
            card.Orientation = orientation;
            return CommandOutcome.Success;
        }

        public CommandOutcome SellSquad(string playerId, string cardId)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            var card = s.Cards.FirstOrDefault(c => c.CardId == cardId);
            if (card == null) return CommandOutcome.Fail("unknownCard", "no card");
            if (card.PurchasedRound != CurrentRound) return CommandOutcome.Fail("notThisRoundPurchase", "not this round");
            s.Cards.Remove(card);
            s.Coin += card.Invested;
            s.DeploysRemaining += 1;
            return CommandOutcome.Success;
        }

        public CommandOutcome UnlockUnit(string playerId, string unitId)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            if (!_catalog.TryGetUnit(unitId, out var unit)) return CommandOutcome.Fail("unknownUnit", "no unit");
            if (s.UnlockedUnits.Contains(unitId)) return CommandOutcome.Fail("alreadyUnlocked", "already unlocked");
            if (s.UnlocksRemaining <= 0) return CommandOutcome.Fail("noUnlocksLeft", "no unlocks");
            if (s.Coin < unit.Cost.UnlockCost) return CommandOutcome.Fail("insufficientFunds", "not enough coin");
            s.Coin -= unit.Cost.UnlockCost;
            s.UnlocksRemaining -= 1;
            s.UnlockedUnits.Add(unitId);
            return CommandOutcome.Success;
        }

        public CommandOutcome BuyTech(string playerId, string unitId, string techId)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            if (!_catalog.TryGetUnit(unitId, out var unit)) return CommandOutcome.Fail("unknownUnit", "no unit");
            var tech = unit.Techs.FirstOrDefault(t => t.Id == techId);
            if (tech == null) return CommandOutcome.Fail("unknownTech", "no tech");
            if (s.PurchasedTechs.Contains(techId)) return CommandOutcome.Fail("techAlreadyOwned", "already owned");
            int price = s.TechPriceByUnit.GetValueOrDefault(unitId, tech.Cost);
            if (s.Coin < price) return CommandOutcome.Fail("insufficientFunds", "not enough coin");
            s.Coin -= price;
            s.PurchasedTechs.Add(techId);
            s.TechPriceByUnit[unitId] = s.TechPriceByUnit.GetValueOrDefault(unitId, tech.Cost) + _rules.TechPriceEscalation;
            return CommandOutcome.Success;
        }

        public CommandOutcome BuyLevel(string playerId, string cardId)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            var card = s.Cards.FirstOrDefault(c => c.CardId == cardId);
            if (card == null) return CommandOutcome.Fail("unknownCard", "no card");
            if (!_catalog.TryGetUnit(card.UnitId, out var unit)) return CommandOutcome.Fail("unknownUnit", "unit missing");
            if (card.Xp < unit.Squad.XpToLevel) return CommandOutcome.Fail("xpNotReady", "not enough xp");
            int cost = (int)Math.Round(unit.Cost.DeployCost * _rules.Leveling.UpgradeCostFraction);
            if (s.Coin < cost) return CommandOutcome.Fail("insufficientFunds", "not enough coin");
            s.Coin -= cost;
            card.Xp -= unit.Squad.XpToLevel;
            card.Level += 1;
            card.Invested += cost;
            return CommandOutcome.Success;
        }

        public CommandOutcome SetReady(string playerId, bool ready, long now)
        {
            var s = RequirePlanningSeat(playerId, out var err);
            if (s == null) return err;
            s.Ready = ready;
            if (_seats.All(x => x.Ready)) PlanLock(now);
            return CommandOutcome.Success;
        }

        // -------------------------------------------------------------------
        // Snapshots (DTOs)
        // -------------------------------------------------------------------

        public MatchConfigDto MatchConfig() => new MatchConfigDto
        {
            Board = new BoardWH { W = _rules.Board.W, H = _rules.Board.H },
            DeploysPerRound = _rules.DeploysPerRound,
            UnlocksPerRound = _rules.UnlocksPerRound,
            IncomePerRoundIncrement = _rules.Income.PerRoundIncrement,
            DeploySeconds = _rules.Timers.DeploySeconds,
            BattleSeconds = _rules.Timers.BattleSeconds,
            CommanderPickSeconds = _rules.Timers.CommanderPickSeconds,
            CommandersOffered = _rules.CommandersOffered,
        };

        public MatchSnapshotDto SnapshotFor(int seat)
        {
            var own = _seats[seat];
            var opp = _seats[seat == 0 ? 1 : 0];
            return new MatchSnapshotDto
            {
                Phase = PhaseToString(CurrentPhase),
                Round = CurrentRound,
                PhaseDeadline = PhaseDeadline,
                CommanderOffers = own.CommanderOffers,
                Own = SeatViewDtoOf(own),
                Opponent = (opp.Connected || opp.PlayerId != null) ? OpponentViewDtoOf(opp) : null,
            };
        }

        public RoundResultData? LastRoundResult()
        {
            if (_lastResult == null) return null;
            _lastResult.Hp = _seats.Select(s => (s.Seat, s.Hp)).ToList();
            return _lastResult;
        }

        public List<(int seat, List<SquadCardDto> cards)> RevealArmies()
            => _seats.Select(s => (s.Seat, s.RevealedCards.Select(ToDto).ToList())).ToList();

        public List<(int seat, int hp)> FinalHp() => _seats.Select(s => (s.Seat, s.Hp)).ToList();

        private SeatViewDto SeatViewDtoOf(SeatState s) => new SeatViewDto
        {
            Seat = s.Seat,
            PlayerName = s.PlayerName,
            Connected = s.Connected,
            CommanderId = s.CommanderId,
            Hp = s.Hp,
            Coin = s.Coin,
            DeploysRemaining = s.DeploysRemaining,
            UnlocksRemaining = s.UnlocksRemaining,
            Ready = s.Ready,
            Cards = s.Cards.Select(ToDto).ToList(),
            Tech = new SeatTechStateDto
            {
                UnlockedUnits = s.UnlockedUnits.ToList(),
                PurchasedTechs = s.PurchasedTechs.ToList(),
                TechPriceByUnit = new Dictionary<string, int>(s.TechPriceByUnit),
            },
        };

        private OpponentViewDto OpponentViewDtoOf(SeatState s) => new OpponentViewDto
        {
            Seat = s.Seat,
            PlayerName = s.PlayerName,
            Connected = s.Connected,
            CommanderId = s.CommanderId,
            Hp = s.Hp,
            Cards = s.RevealedCards.Select(ToDto).ToList(),
        };

        private static SquadCardDto ToDto(SquadCardData c) => new SquadCardDto
        {
            CardId = c.CardId,
            UnitId = c.UnitId,
            Anchor = new WireAnchor { Row = c.Anchor.Row, Col = c.Anchor.Col },
            Orientation = c.Orientation,
            Level = c.Level,
            Xp = c.Xp,
            PurchasedRound = c.PurchasedRound,
            Invested = c.Invested,
        };

        // -------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------

        private SeatState? RequireSeat(string playerId) => _seats.FirstOrDefault(s => s.PlayerId == playerId);

        private SeatState? RequirePlanningSeat(string playerId, out CommandOutcome err)
        {
            var s = RequireSeat(playerId);
            if (s == null) { err = CommandOutcome.Fail("notJoined", "not in this room"); return null; }
            if (CurrentPhase != Phase.Planning) { err = CommandOutcome.Fail("wrongPhase", "not in planning"); return null; }
            err = CommandOutcome.Success;
            return s;
        }

        private void AddCard(SeatState s, string unitId, int row, int col, string orientation, int invested, bool free)
        {
            s.Cards.Add(new SquadCardData
            {
                CardId = $"sq{_nextCardId++}",
                UnitId = unitId,
                Anchor = new SquadCardData.AnchorData { Row = row, Col = col },
                Orientation = orientation,
                Level = 1,
                Xp = 0,
                PurchasedRound = free ? 0 : CurrentRound,
                Invested = invested,
            });
        }

        private CommandOutcome? ValidatePlacement(SeatState s, UnitDef unit, int row, int col, string? ignoreCardId)
        {
            var board = _rules.Board;
            if (!Footprints.FitsBoard(unit, row, col, board.W, board.H))
                return CommandOutcome.Fail("outOfBounds", $"does not fit {board.W}x{board.H}");
            var tiles = Footprints.Tiles(unit, row, col);
            if (unit.Placement.Domain != Domain.Building && !WithinSeatZones(s.Seat, tiles))
                return CommandOutcome.Fail("outsideOwnHalf", "outside your deploy zone");
            foreach (var other in s.Cards)
            {
                if (other.CardId == ignoreCardId) continue;
                if (!_catalog.TryGetUnit(other.UnitId, out var ou)) continue;
                var ot = Footprints.Tiles(ou, other.Anchor.Row, other.Anchor.Col);
                if (tiles.Overlaps(ot)) return CommandOutcome.Fail("tileOccupied", $"overlaps {other.CardId}");
            }
            return null;
        }

        private bool WithinSeatZones(int seat, TileRect tiles)
        {
            foreach (var zone in _rules.DeployZones)
            {
                if (zone.Seat != seat) continue;
                var r = zone.Rect;
                if (tiles.RowStart >= r.Row && tiles.RowEnd <= r.Row + r.W - 1 &&
                    tiles.ColStart >= r.Col && tiles.ColEnd <= r.Col + r.H - 1)
                    return true;
            }
            return false;
        }

        private static SquadCardData CloneCard(SquadCardData c) => new SquadCardData
        {
            CardId = c.CardId,
            UnitId = c.UnitId,
            Anchor = new SquadCardData.AnchorData { Row = c.Anchor.Row, Col = c.Anchor.Col },
            Orientation = c.Orientation,
            Level = c.Level,
            Xp = c.Xp,
            PurchasedRound = c.PurchasedRound,
            Invested = c.Invested,
        };

        private static List<T> Rotate<T>(List<T> arr, int by)
        {
            if (arr.Count == 0) return arr;
            int n = ((by % arr.Count) + arr.Count) % arr.Count;
            return arr.Skip(n).Concat(arr.Take(n)).ToList();
        }

        private static Orientation ParseOrientation(string s) => s switch
        {
            "east" => Orientation.East,
            "south" => Orientation.South,
            "west" => Orientation.West,
            _ => Orientation.North,
        };

        private static string OrientationToString(Orientation o) => o switch
        {
            Orientation.East => "east",
            Orientation.South => "south",
            Orientation.West => "west",
            _ => "north",
        };

        private static string PhaseToString(Phase p) => p switch
        {
            Phase.CommanderPick => "commanderPick",
            Phase.Planning => "planning",
            Phase.Battle => "battle",
            Phase.Results => "results",
            Phase.MatchEnded => "matchEnded",
            _ => "lobby",
        };

        // Internal mutable state (never serialized directly; DTOs above are).
        private sealed class SeatState
        {
            public int Seat;
            public string? PlayerId;
            public string PlayerName = "";
            public string ResumeToken = "";
            public bool Connected;
            public string? CommanderId;
            public List<string> CommanderOffers = new List<string>();
            public int Hp;
            public int Coin;
            public int DeploysRemaining;
            public int UnlocksRemaining;
            public bool Ready;
            public List<SquadCardData> Cards = new List<SquadCardData>();
            public HashSet<string> UnlockedUnits = new HashSet<string>();
            public HashSet<string> PurchasedTechs = new HashSet<string>();
            public Dictionary<string, int> TechPriceByUnit = new Dictionary<string, int>();
            public List<SquadCardData> RevealedCards = new List<SquadCardData>();
        }

        private sealed class PendingBattle
        {
            public List<(int seat, List<SquadCardData> cards)> Armies = new List<(int, List<SquadCardData>)>();
            public HashSet<int> Acked = new HashSet<int>();
            public Sim.BattleResult? SimResult;
        }

        public sealed class RoundResultData
        {
            public int Round;
            public int? WinnerSeat;
            public List<(int seat, int amount)> HpDamage = new List<(int, int)>();
            public List<(int seat, int hp)> Hp = new List<(int, int)>();
        }

        public sealed class SquadCardData
        {
            public string CardId = "";
            public string UnitId = "";
            public AnchorData Anchor = new AnchorData();
            public string Orientation = "north";
            public int Level = 1;
            public double Xp;
            public int PurchasedRound;
            public int Invested;
            public sealed class AnchorData { public int Row; public int Col; }
        }
    }

    /// <summary>Serializable full state of a MatchRoom (for SQLite persistence).</summary>
    public sealed class MatchRoomSnapshot
    {
        public string Id { get; set; } = "";
        public string Phase { get; set; } = "lobby";
        public int Round { get; set; }
        public long PhaseDeadline { get; set; }
        public int NextCardId { get; set; }
        public int? Winner { get; set; }
        public List<SeatSnapshot> Seats { get; set; } = new List<SeatSnapshot>();

        public sealed class SeatSnapshot
        {
            public int Seat { get; set; }
            public string PlayerName { get; set; } = "";
            public string ResumeToken { get; set; } = "";
            public string? CommanderId { get; set; }
            public List<string> CommanderOffers { get; set; } = new List<string>();
            public int Hp { get; set; }
            public int Coin { get; set; }
            public int DeploysRemaining { get; set; }
            public int UnlocksRemaining { get; set; }
            public bool Ready { get; set; }
            public List<MatchRoom.SquadCardData> Cards { get; set; } = new List<MatchRoom.SquadCardData>();
            public List<MatchRoom.SquadCardData> RevealedCards { get; set; } = new List<MatchRoom.SquadCardData>();
            public List<string> UnlockedUnits { get; set; } = new List<string>();
            public List<string> PurchasedTechs { get; set; } = new List<string>();
            public Dictionary<string, int> TechPriceByUnit { get; set; } = new Dictionary<string, int>();
        }
    }
}
