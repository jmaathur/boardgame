using System;
using System.Collections.Generic;
using BoardGame.Core.Catalog;
using BoardGame.Core.Events;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Sim
{
    public sealed class BattleResult
    {
        /// <summary>0, 1, or -1 for a draw / timeout.</summary>
        public int WinnerSeat;
        public int DurationTicks;
        /// <summary>Prorated survivor value each seat inflicts as HP damage.</summary>
        public Dictionary<int, int> SurvivorValueBySeat = new Dictionary<int, int>();
        /// <summary>Members alive per seat at the end (for tables).</summary>
        public Dictionary<int, int> MembersAliveBySeat = new Dictionary<int, int>();
    }

    /// <summary>
    /// The battle simulation (design doc §6). Fixed 20 Hz tick, seeded xorshift
    /// consumed in a fixed phase order, dense id iteration → deterministic. One
    /// full battle resolves in one burst; clients play back the event log.
    ///
    /// v1 covers the base-pack behavior: melee/ranged instant weapons, volley
    /// (as spaced instant sub-shots), beam (ramping per-tick), splash via
    /// areaDamage onImpact, aura statuses (damage buffs), shields, per-member HP,
    /// steering movement, and XP-driven survivor valuation. The vocabulary grows
    /// with content (design doc §11.6).
    /// </summary>
    public sealed class BattleSim
    {
        public const int TickRate = 20;
        public const int MaxBattleSeconds = 120;

        private readonly LoadedCatalog _catalog;
        private readonly IBattleEventSink _sink;
        private readonly XorShiftRng _rng;
        private readonly int _maxTicks;
        private readonly List<SquadState> _squads = new List<SquadState>();
        private int _nextSquadId = 1;
        private int _tick;

        // Per-tick damage buffer: all weapons/effects in a tick accumulate here
        // and flush together, so processing order between the two seats does not
        // bias the outcome (a symmetric clash stays symmetric). Keyed by the
        // member; carries a squad ref for the event emit.
        private readonly Dictionary<MemberState, (SquadState squad, double amount)> _pendingDamage
            = new Dictionary<MemberState, (SquadState, double)>();
        private bool _buffering;

        public BattleSim(LoadedCatalog catalog, uint seed, IBattleEventSink? sink = null, int? maxBattleSeconds = null)
        {
            _catalog = catalog;
            _sink = sink ?? NullEventSink.Instance;
            _rng = new XorShiftRng(seed);
            _maxTicks = (maxBattleSeconds ?? MaxBattleSeconds) * TickRate;
            Seed = seed;
        }

        public uint Seed { get; }

        public BattleResult Run(ArmyBlueprint armyA, ArmyBlueprint armyB)
        {
            _sink.Emit(new BattleStartedEvent { Tick = 0, Seed = Seed, TickRate = TickRate, DurationTicks = _maxTicks });
            SpawnArmy(armyA);
            SpawnArmy(armyB);

            int winner = -1;
            string reason = "timeout";
            for (_tick = 1; _tick <= _maxTicks; _tick++)
            {
                TickStatuses();
                ApplyAuras();
                TickTargeting();
                TickMovement();
                _buffering = true;
                TickWeapons();
                _buffering = false;
                FlushDamage();
                EmitKeyframes();
                int alive0 = AliveMembers(0), alive1 = AliveMembers(1);
                if (alive0 == 0 || alive1 == 0)
                {
                    winner = alive0 == 0 && alive1 == 0 ? -1 : (alive0 > 0 ? 0 : 1);
                    reason = "wipe";
                    break;
                }
            }
            if (_tick > _maxTicks) _tick = _maxTicks;

            _sink.Emit(new BattleEndedEvent { Tick = _tick, WinnerSeat = winner, Reason = reason });

            return BuildResult(winner);
        }

        // -------------------------------------------------------------------
        // Setup
        // -------------------------------------------------------------------

        private void SpawnArmy(ArmyBlueprint army)
        {
            foreach (var bp in army.Squads)
            {
                if (!_catalog.TryGetUnit(bp.UnitId, out var unit)) continue;
                var squad = new SquadState
                {
                    BattleSquadId = _nextSquadId++,
                    Seat = army.Seat,
                    Unit = unit,
                    Level = bp.Level,
                    Invested = bp.Invested,
                    CardId = bp.CardId,
                };
                var offsets = Footprints.OrientedFormation(unit, bp.Orientation);
                // Anchor is the footprint min corner; center is anchor + oriented half-size.
                var (ow, oh) = Footprints.OrientedSize(unit.Placement.Footprint, bp.Orientation);
                double cx = bp.AnchorRow + ow / 2.0;
                double cz = bp.AnchorCol + oh / 2.0;
                double hp = unit.Member.Hp * HpFactor(bp.Level);
                for (int i = 0; i < offsets.Count; i++)
                {
                    squad.Members.Add(new MemberState
                    {
                        Index = i,
                        X = cx + offsets[i].X,
                        Z = cz + offsets[i].Z,
                        Hp = hp,
                        MaxHp = hp,
                        WeaponCooldown = new int[unit.Member.Weapons.Count],
                    });
                }
                _squads.Add(squad);
                _sink.Emit(new SquadSpawnedEvent
                {
                    Tick = _tick,
                    BattleSquadId = squad.BattleSquadId,
                    Seat = squad.Seat,
                    UnitId = unit.Id,
                    Level = squad.Level,
                    Members = MembersSnapshot(squad),
                });
            }
        }

        private static List<MemberSpawn> MembersSnapshot(SquadState squad)
        {
            var list = new List<MemberSpawn>(squad.Members.Count);
            foreach (var m in squad.Members)
                list.Add(new MemberSpawn { Index = m.Index, X = m.X, Z = m.Z, Hp = m.Hp });
            return list;
        }

        // -------------------------------------------------------------------
        // Phase A: statuses / DoT expiry
        // -------------------------------------------------------------------

        private void TickStatuses()
        {
            foreach (var squad in _squads)
            {
                foreach (var m in squad.Members)
                {
                    if (!m.Alive || m.Statuses.Count == 0) continue;
                    // DoT + decrement in a stable key order for determinism.
                    var keys = new List<string>(m.Statuses.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (var statusId in keys)
                    {
                        if (_catalog.TryGetStatus(statusId, out var status) && status.Dot != null)
                        {
                            ApplyDamage(squad, m, status.Dot.AmountPerSecond / TickRate, "dot");
                        }
                        int rem = m.Statuses[statusId];
                        if (rem != int.MaxValue)
                        {
                            rem--;
                            if (rem <= 0) m.Statuses.Remove(statusId);
                            else m.Statuses[statusId] = rem;
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        // Phase C: targeting + steering movement
        // -------------------------------------------------------------------

        /// <summary>
        /// Every member (re)acquires its target from the FROZEN start-of-tick
        /// state, in one pass before any movement or firing. Doing this in its
        /// own phase means no member's target choice depends on another member's
        /// same-tick move/kill — the last order-dependence that biased symmetric
        /// clashes toward the first-iterated seat.
        /// </summary>
        private void TickTargeting()
        {
            foreach (var squad in _squads)
            {
                var weapon = squad.Unit.Member.Weapons.Count > 0 ? squad.Unit.Member.Weapons[0] : null;
                foreach (var m in squad.Members)
                {
                    if (!m.Alive) continue;
                    if (!TargetAlive(m)) AcquireTargetForWeapon(squad, m, weapon);
                }
            }
        }

        private void TickMovement()
        {
            // Decide all moves against the (now targeted) start-of-tick positions,
            // then apply them simultaneously.
            var moves = new List<(MemberState m, double nx, double nz)>();
            foreach (var squad in _squads)
            {
                double speed = squad.Unit.Member.Speed / TickRate;
                double range = MaxWeaponRange(squad.Unit);
                foreach (var m in squad.Members)
                {
                    if (!m.Alive || m.TargetSquad < 0) continue;
                    var tSquad = SquadById(m.TargetSquad);
                    if (tSquad == null) continue;
                    var tm = tSquad.Members[m.TargetMember];
                    double dx = tm.X - m.X, dz = tm.Z - m.Z;
                    double dist = Math.Sqrt(dx * dx + dz * dz);
                    if (dist > range && speed > 0)
                    {
                        double step = Math.Min(speed, dist - range * 0.9);
                        if (step > 0 && dist > 1e-6)
                            moves.Add((m, m.X + dx / dist * step, m.Z + dz / dist * step));
                    }
                }
            }
            foreach (var (m, nx, nz) in moves)
            {
                m.X = nx;
                m.Z = nz;
                m.MovedSinceKeyframe = true;
            }
        }

        private void ApplyAuras()
        {
            foreach (var squad in _squads)
            {
                foreach (var m in squad.Members)
                {
                    if (!m.Alive) continue;
                    foreach (var ability in squad.Unit.Member.Abilities)
                    {
                        if (ability.Trigger is not AuraTrigger aura) continue;
                        double r2 = aura.Radius * aura.Radius;
                        foreach (var other in _squads)
                        {
                            bool ally = other.Seat == squad.Seat;
                            if (aura.Filter.Side == "ally" && !ally) continue;
                            if (aura.Filter.Side == "enemy" && ally) continue;
                            foreach (var om in other.Members)
                            {
                                if (!om.Alive) continue;
                                double dx = om.X - m.X, dz = om.Z - m.Z;
                                if (dx * dx + dz * dz > r2) continue;
                                foreach (var eff in ability.Effects)
                                    ApplyEffect(other, om, squad, m, eff);
                            }
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        // Phase D: weapons
        // -------------------------------------------------------------------

        private void TickWeapons()
        {
            foreach (var squad in _squads)
            {
                var weapons = squad.Unit.Member.Weapons;
                if (weapons.Count == 0) continue;
                foreach (var m in squad.Members)
                {
                    if (!m.Alive) continue;
                    // Fire at the target acquired this tick's targeting phase — no
                    // mid-weapon-phase retargeting (that reintroduced order bias).
                    if (m.TargetSquad < 0) continue;
                    var tSquad = SquadById(m.TargetSquad);
                    if (tSquad == null || !TargetAlive(m)) continue;
                    var tm = tSquad.Members[m.TargetMember];
                    for (int wi = 0; wi < weapons.Count; wi++)
                    {
                        if (m.WeaponCooldown[wi] > 0) { m.WeaponCooldown[wi]--; continue; }
                        var weapon = weapons[wi];
                        if (!CanTarget(weapon, tSquad.Unit)) continue;
                        if (!InWeaponRange(m, tm, weapon)) continue;
                        FireWeapon(squad, m, wi, weapon, tSquad, tm);
                        int cd = Math.Max(1, (int)Math.Round(weapon.Interval * TickRate));
                        m.WeaponCooldown[wi] = cd;
                    }
                }
            }
        }

        private void FireWeapon(SquadState squad, MemberState m, int wi, Weapon weapon, SquadState tSquad, MemberState tm)
        {
            _sink.Emit(new AttackFiredEvent
            {
                Tick = _tick,
                BattleSquadId = squad.BattleSquadId,
                MemberIndex = m.Index,
                WeaponId = weapon.Id,
                TargetSquadId = tSquad.BattleSquadId,
                TargetMemberIndex = tm.Index,
            });

            int shots = weapon.Fire is VolleyFire v ? Math.Max(1, v.Count) : 1;
            double perShot = ScaledWeaponDamage(weapon, squad.Level);
            for (int s = 0; s < shots; s++)
            {
                // Splash / areaDamage onImpact effects.
                bool hadArea = false;
                foreach (var eff in weapon.OnImpact)
                {
                    if (eff is AreaDamageEffect area)
                    {
                        hadArea = true;
                        ApplyAreaDamage(squad, tm.X, tm.Z, area, squad.Level);
                    }
                    else
                    {
                        ApplyEffect(tSquad, tm, squad, m, eff);
                    }
                }
                if (!hadArea && perShot > 0)
                {
                    ApplyDamage(tSquad, tm, ScaledDamageWithBuffs(squad, m, perShot), "hit");
                }
            }
            AwardXpOnHit(squad, tSquad, perShot);
        }

        private void ApplyAreaDamage(SquadState source, double x, double z, AreaDamageEffect area, int level)
        {
            double baseAmt = area.Amount.At(level);
            double r2 = area.Radius * area.Radius;
            foreach (var squad in _squads)
            {
                if (squad.Seat == source.Seat) continue;
                foreach (var m in squad.Members)
                {
                    if (!m.Alive) continue;
                    double dx = m.X - x, dz = m.Z - z;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > r2) continue;
                    double amt = baseAmt;
                    if (area.Falloff == Falloff.Linear && area.Radius > 0)
                    {
                        double d = Math.Sqrt(d2);
                        amt *= Math.Max(0.0, 1.0 - d / area.Radius);
                    }
                    ApplyDamage(squad, m, amt, "splash");
                }
            }
        }

        // -------------------------------------------------------------------
        // Effects
        // -------------------------------------------------------------------

        private void ApplyEffect(SquadState targetSquad, MemberState target, SquadState sourceSquad, MemberState source, Effect eff)
        {
            switch (eff)
            {
                case DamageEffect d:
                    ApplyDamage(targetSquad, target, ScaledDamageWithBuffs(sourceSquad, source, d.Amount.At(sourceSquad.Level)), "hit");
                    break;
                case HealEffect h:
                    target.Hp = Math.Min(target.MaxHp, target.Hp + h.Amount.At(sourceSquad.Level));
                    break;
                case GrantShieldEffect g:
                    target.Shield += g.Amount.At(sourceSquad.Level);
                    break;
                case ApplyStatusEffect a:
                    ApplyStatus(targetSquad, target, a);
                    break;
                // spawnUnits, modifySelf, areaDamage handled elsewhere or later.
            }
        }

        private void ApplyStatus(SquadState squad, MemberState m, ApplyStatusEffect a)
        {
            int ticks = a.DurationS.HasValue && a.DurationS.Value > 0
                ? Math.Max(1, (int)Math.Round(a.DurationS.Value * TickRate))
                : int.MaxValue;
            bool isNew = !m.Statuses.ContainsKey(a.StatusId);
            m.Statuses[a.StatusId] = ticks;
            if (isNew)
            {
                _sink.Emit(new StatusAppliedEvent
                {
                    Tick = _tick,
                    BattleSquadId = squad.BattleSquadId,
                    MemberIndex = m.Index,
                    StatusId = a.StatusId,
                });
            }
        }

        // -------------------------------------------------------------------
        // Damage pipeline
        // -------------------------------------------------------------------

        private void ApplyDamage(SquadState squad, MemberState m, double raw, string kind)
        {
            if (!m.Alive || raw <= 0) return;
            if (_buffering)
            {
                // Accumulate; the pipeline + death resolve together at flush so
                // the two seats' weapon-processing order never biases the result.
                if (_pendingDamage.TryGetValue(m, out var prev))
                    _pendingDamage[m] = (squad, prev.amount + raw);
                else
                    _pendingDamage[m] = (squad, raw);
                return;
            }
            ResolveDamage(squad, m, raw);
        }

        private void FlushDamage()
        {
            if (_pendingDamage.Count == 0) return;
            // Deterministic apply order: by squad id then member index.
            var entries = new List<(MemberState m, SquadState squad, double amount)>();
            foreach (var kv in _pendingDamage)
                entries.Add((kv.Key, kv.Value.squad, kv.Value.amount));
            _pendingDamage.Clear();
            entries.Sort((a, b) =>
            {
                int c = a.squad.BattleSquadId.CompareTo(b.squad.BattleSquadId);
                return c != 0 ? c : a.m.Index.CompareTo(b.m.Index);
            });
            foreach (var (m, squad, amount) in entries)
                ResolveDamage(squad, m, amount);
        }

        /// <summary>Run the damage pipeline against a member and emit events.</summary>
        private void ResolveDamage(SquadState squad, MemberState m, double raw)
        {
            if (!m.Alive || raw <= 0) return;
            double dmg = Math.Max(1.0, raw * DamageTakenMul(m)) - squad.Unit.Member.FlatBlock;
            if (dmg <= 0) return;

            string layer = "hull";
            if (m.Shield > 0)
            {
                double absorbed = Math.Min(m.Shield, dmg);
                m.Shield -= absorbed;
                dmg -= absorbed;
                _sink.Emit(new DamageAppliedEvent
                {
                    Tick = _tick,
                    BattleSquadId = squad.BattleSquadId,
                    MemberIndex = m.Index,
                    Layer = "shield",
                    Amount = absorbed,
                    HpAfter = m.Hp,
                });
                if (dmg <= 0) return;
            }

            m.Hp -= dmg;
            _sink.Emit(new DamageAppliedEvent
            {
                Tick = _tick,
                BattleSquadId = squad.BattleSquadId,
                MemberIndex = m.Index,
                Layer = layer,
                Amount = dmg,
                HpAfter = Math.Max(0, m.Hp),
            });
            if (m.Hp <= 0)
            {
                m.Hp = 0;
                _sink.Emit(new MemberDiedEvent { Tick = _tick, BattleSquadId = squad.BattleSquadId, MemberIndex = m.Index });
            }
        }

        // -------------------------------------------------------------------
        // Targeting helpers
        // -------------------------------------------------------------------

        private void AcquireTargetForWeapon(SquadState squad, MemberState m, Weapon? weapon)
        {
            double best = double.MaxValue;
            int bestSquad = -1, bestMember = -1;
            foreach (var other in _squads)
            {
                if (other.Seat == squad.Seat) continue;
                if (weapon != null && !CanTarget(weapon, other.Unit)) continue;
                foreach (var om in other.Members)
                {
                    if (!om.Alive) continue;
                    double dx = om.X - m.X, dz = om.Z - m.Z;
                    double d2 = dx * dx + dz * dz;
                    // id tiebreak keeps it deterministic.
                    if (d2 < best || (d2 == best && (other.BattleSquadId < bestSquad || (other.BattleSquadId == bestSquad && om.Index < bestMember))))
                    {
                        best = d2;
                        bestSquad = other.BattleSquadId;
                        bestMember = om.Index;
                    }
                }
            }
            m.TargetSquad = bestSquad;
            m.TargetMember = bestMember;
        }

        private bool TargetAlive(MemberState m)
        {
            if (m.TargetSquad < 0) return false;
            var s = SquadById(m.TargetSquad);
            if (s == null || m.TargetMember < 0 || m.TargetMember >= s.Members.Count) return false;
            return s.Members[m.TargetMember].Alive;
        }

        private static bool CanTarget(Weapon weapon, UnitDef targetUnit)
        {
            var domain = targetUnit.Placement.Domain;
            foreach (var t in weapon.Targets)
            {
                if (t == TargetDomain.Air && domain == Domain.Air) return true;
                if (t == TargetDomain.Ground && (domain == Domain.Ground || domain == Domain.Building)) return true;
            }
            return false;
        }

        private static bool InWeaponRange(MemberState m, MemberState tm, Weapon weapon)
        {
            double dx = tm.X - m.X, dz = tm.Z - m.Z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            return d <= weapon.Range && d >= weapon.MinRange;
        }

        private static double MaxWeaponRange(UnitDef unit)
        {
            double r = 1.0;
            foreach (var w in unit.Member.Weapons) if (w.Range > r) r = w.Range;
            return r;
        }

        // -------------------------------------------------------------------
        // Stat / XP helpers
        // -------------------------------------------------------------------

        private double HpFactor(int level) => 1.0 + _catalog.MatchRules.Leveling.HpFactorPerLevel * (level - 1);
        private double AtkFactor(int level) => 1.0 + _catalog.MatchRules.Leveling.AtkFactorPerLevel * (level - 1);

        private double ScaledWeaponDamage(Weapon weapon, int level)
        {
            double baseDmg = weapon.Damage?.At(level) ?? 0;
            return baseDmg * AtkFactor(level);
        }

        private double ScaledDamageWithBuffs(SquadState squad, MemberState m, double baseDmg)
        {
            double mul = 1.0;
            foreach (var kv in m.Statuses)
            {
                if (_catalog.TryGetStatus(kv.Key, out var status))
                {
                    foreach (var mod in status.Mods)
                    {
                        if (mod.Stat == ModStat.Damage && mod.AddPct.HasValue)
                            mul += mod.AddPct.Value / 100.0;
                    }
                }
            }
            return baseDmg * mul;
        }

        private double DamageTakenMul(MemberState m)
        {
            double mul = 1.0;
            foreach (var kv in m.Statuses)
            {
                if (_catalog.TryGetStatus(kv.Key, out var status))
                {
                    foreach (var mod in status.Mods)
                    {
                        if (mod.Stat == ModStat.DamageTaken)
                        {
                            if (mod.AddPct.HasValue) mul += mod.AddPct.Value / 100.0;
                            if (mod.MulPct.HasValue) mul *= mod.MulPct.Value / 100.0;
                        }
                    }
                }
            }
            return mul;
        }

        private void AwardXpOnHit(SquadState attacker, SquadState victim, double dmg)
        {
            // XP accrual is tracked at squad level for leveling between rounds;
            // in-battle we don't level, so this is a no-op placeholder that keeps
            // the attribution seam. (Between-round XP is applied by the loop.)
        }

        // -------------------------------------------------------------------
        // Keyframes / bookkeeping
        // -------------------------------------------------------------------

        private void EmitKeyframes()
        {
            // Position keyframes every 4th tick (5 Hz), moved members only.
            if (_tick % 4 != 0) return;
            foreach (var squad in _squads)
            {
                List<MemberPos>? moved = null;
                foreach (var m in squad.Members)
                {
                    if (!m.Alive || !m.MovedSinceKeyframe) continue;
                    moved ??= new List<MemberPos>();
                    moved.Add(new MemberPos { Index = m.Index, X = m.X, Z = m.Z });
                    m.MovedSinceKeyframe = false;
                }
                if (moved != null)
                    _sink.Emit(new PositionKeyframesEvent { Tick = _tick, BattleSquadId = squad.BattleSquadId, Positions = moved });
            }
        }

        private int AliveMembers(int seat)
        {
            int n = 0;
            foreach (var squad in _squads) if (squad.Seat == seat) n += squad.AliveCount;
            return n;
        }

        private SquadState? SquadById(int id)
        {
            foreach (var s in _squads) if (s.BattleSquadId == id) return s;
            return null;
        }

        private BattleResult BuildResult(int winner)
        {
            var result = new BattleResult { WinnerSeat = winner, DurationTicks = _tick };
            foreach (var seat in new[] { 0, 1 })
            {
                int survivorValue = 0;
                int membersAlive = 0;
                foreach (var squad in _squads)
                {
                    if (squad.Seat != seat) continue;
                    membersAlive += squad.AliveCount;
                    if (squad.CardId == null || squad.Members.Count == 0) continue;
                    // Prorated by surviving members.
                    survivorValue += (int)Math.Round(
                        squad.Invested * (double)squad.AliveCount / squad.Members.Count);
                }
                result.SurvivorValueBySeat[seat] = survivorValue;
                result.MembersAliveBySeat[seat] = membersAlive;
            }
            return result;
        }
    }
}
