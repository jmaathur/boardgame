using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Hand-maintained C# DTOs mirroring the zod catalog schema in
// core/types/src/catalog-schema.ts. The plan (§9) budgeted a zod→C# emitter but
// capped it at ~400 lines; z.toJSONSchema inlines every union at every use site,
// so a faithful emitter would need structural de-duplication well past that
// budget. Per that same mitigation we hand-maintain these instead. A conformance
// test (CatalogLoaderTests) round-trips the real committed catalog through these
// DTOs, so drift between the JSON and these shapes fails CI.
//
// Keep in sync with catalog-schema.ts; bump CatalogSchema.Version there and here
// together when the wire shapes change.
namespace BoardGame.Core.Generated
{
    public static class CatalogSchema
    {
        public const int Version = 1;
    }

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    /// <summary>number | {base, perLevel}; scales as base + perLevel*(level-1).</summary>
    [JsonConverter(typeof(ScaledValueConverter))]
    public struct ScaledValue
    {
        public double Base { get; set; }
        public double PerLevel { get; set; }
        public double At(int level) => Base + PerLevel * (level - 1);
        public static ScaledValue Flat(double v) => new ScaledValue { Base = v, PerLevel = 0 };
    }

    public sealed class ScaledValueConverter : JsonConverter<ScaledValue>
    {
        public override ScaledValue ReadJson(JsonReader reader, Type objectType, ScaledValue existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartObject)
            {
                var o = JObject.Load(reader);
                return new ScaledValue
                {
                    Base = (double?)o["base"] ?? 0,
                    PerLevel = (double?)o["perLevel"] ?? 0,
                };
            }
            return new ScaledValue { Base = Convert.ToDouble(reader.Value), PerLevel = 0 };
        }

        public override void WriteJson(JsonWriter writer, ScaledValue value, JsonSerializer serializer)
        {
            if (value.PerLevel == 0)
            {
                writer.WriteValue(value.Base);
                return;
            }
            writer.WriteStartObject();
            writer.WritePropertyName("base");
            writer.WriteValue(value.Base);
            writer.WritePropertyName("perLevel");
            writer.WriteValue(value.PerLevel);
            writer.WriteEndObject();
        }
    }

    public enum ModStat { Hp, Damage, Speed, Range, AttackInterval, DamageTaken, Shield }

    public enum Domain { Ground, Air, Building }

    public enum TargetDomain { Ground, Air }

    public enum Falloff { None, Linear }

    public enum Orientation { North, East, South, West }

    public sealed class StatMod
    {
        [JsonProperty("stat")] public ModStat Stat { get; set; }
        [JsonProperty("add")] public double? Add { get; set; }
        [JsonProperty("addPct")] public double? AddPct { get; set; }
        [JsonProperty("mulPct")] public double? MulPct { get; set; }
    }

    public sealed class Filter
    {
        [JsonProperty("side")] public string Side { get; set; } = "any";
        [JsonProperty("domain")] public string DomainFilter { get; set; } = "any";
    }

    // -----------------------------------------------------------------------
    // Effects — discriminated on "kind"
    // -----------------------------------------------------------------------

    [JsonConverter(typeof(EffectConverter))]
    public abstract class Effect
    {
        [JsonProperty("kind")] public abstract string Kind { get; }
    }

    public sealed class DamageEffect : Effect
    {
        public override string Kind => "damage";
        [JsonProperty("amount")] public ScaledValue Amount { get; set; }
    }

    public sealed class AreaDamageEffect : Effect
    {
        public override string Kind => "areaDamage";
        [JsonProperty("amount")] public ScaledValue Amount { get; set; }
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("falloff")] public Falloff Falloff { get; set; }
    }

    public sealed class ApplyStatusEffect : Effect
    {
        public override string Kind => "applyStatus";
        [JsonProperty("statusId")] public string StatusId { get; set; } = "";
        [JsonProperty("durationS")] public double? DurationS { get; set; }
    }

    public sealed class HealEffect : Effect
    {
        public override string Kind => "heal";
        [JsonProperty("amount")] public ScaledValue Amount { get; set; }
    }

    public sealed class GrantShieldEffect : Effect
    {
        public override string Kind => "grantShield";
        [JsonProperty("amount")] public ScaledValue Amount { get; set; }
    }

    public sealed class SpawnUnitsEffect : Effect
    {
        public override string Kind => "spawnUnits";
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("count")] public int Count { get; set; }
        // number | "inherit"
        [JsonProperty("level")] public JToken Level { get; set; } = JValue.CreateNull();
        [JsonProperty("placement")] public string Placement { get; set; } = "aroundSelf";
    }

    public sealed class ModifySelfEffect : Effect
    {
        public override string Kind => "modifySelf";
        [JsonProperty("mods")] public List<StatMod> Mods { get; set; } = new List<StatMod>();
        [JsonProperty("durationS")] public double? DurationS { get; set; }
    }

    public sealed class EffectConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(Effect).IsAssignableFrom(t);
        public override bool CanWrite => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JObject.Load(reader);
            var kind = (string?)token["kind"];
            Effect target = kind switch
            {
                "damage" => new DamageEffect(),
                "areaDamage" => new AreaDamageEffect(),
                "applyStatus" => new ApplyStatusEffect(),
                "heal" => new HealEffect(),
                "grantShield" => new GrantShieldEffect(),
                "spawnUnits" => new SpawnUnitsEffect(),
                "modifySelf" => new ModifySelfEffect(),
                _ => throw new JsonSerializationException($"unknown Effect kind '{kind}'"),
            };
            serializer.Populate(token.CreateReader(), target);
            return target;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    // -----------------------------------------------------------------------
    // Triggers — discriminated on "kind"
    // -----------------------------------------------------------------------

    [JsonConverter(typeof(TriggerConverter))]
    public abstract class Trigger
    {
        [JsonProperty("kind")] public abstract string Kind { get; }
    }

    public sealed class OnSpawnTrigger : Trigger { public override string Kind => "onSpawn"; }
    public sealed class OnDeathTrigger : Trigger { public override string Kind => "onDeath"; }
    public sealed class OnKillTrigger : Trigger { public override string Kind => "onKill"; }
    public sealed class OnDamagedTrigger : Trigger { public override string Kind => "onDamaged"; }

    public sealed class OnHpBelowTrigger : Trigger
    {
        public override string Kind => "onHpBelow";
        [JsonProperty("pct")] public double Pct { get; set; }
        [JsonProperty("once")] public bool Once { get; set; } = true;
    }

    public sealed class PeriodicTrigger : Trigger
    {
        public override string Kind => "periodic";
        [JsonProperty("intervalS")] public double IntervalS { get; set; }
        [JsonProperty("startDelayS")] public double StartDelayS { get; set; }
        [JsonProperty("charges")] public int? Charges { get; set; }
    }

    public sealed class AuraTrigger : Trigger
    {
        public override string Kind => "aura";
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("refreshS")] public double RefreshS { get; set; }
        [JsonProperty("filter")] public Filter Filter { get; set; } = new Filter();
    }

    public sealed class TriggerConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(Trigger).IsAssignableFrom(t);
        public override bool CanWrite => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JObject.Load(reader);
            var kind = (string?)token["kind"];
            Trigger target = kind switch
            {
                "onSpawn" => new OnSpawnTrigger(),
                "onDeath" => new OnDeathTrigger(),
                "onKill" => new OnKillTrigger(),
                "onDamaged" => new OnDamagedTrigger(),
                "onHpBelow" => new OnHpBelowTrigger(),
                "periodic" => new PeriodicTrigger(),
                "aura" => new AuraTrigger(),
                _ => throw new JsonSerializationException($"unknown Trigger kind '{kind}'"),
            };
            serializer.Populate(token.CreateReader(), target);
            return target;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    // -----------------------------------------------------------------------
    // Weapons
    // -----------------------------------------------------------------------

    [JsonConverter(typeof(FireModeConverter))]
    public abstract class FireMode
    {
        [JsonProperty("mode")] public abstract string Mode { get; }
    }

    public sealed class InstantFire : FireMode { public override string Mode => "instant"; }

    public sealed class VolleyFire : FireMode
    {
        public override string Mode => "volley";
        [JsonProperty("count")] public int Count { get; set; }
        [JsonProperty("spacingS")] public double SpacingS { get; set; }
        [JsonProperty("spread")] public double Spread { get; set; }
    }

    public sealed class BeamRamp
    {
        [JsonProperty("addPctPerTick")] public double AddPctPerTick { get; set; }
        [JsonProperty("maxPct")] public double MaxPct { get; set; }
        [JsonProperty("resetOnTargetSwitch")] public bool ResetOnTargetSwitch { get; set; } = true;
    }

    public sealed class BeamFire : FireMode
    {
        public override string Mode => "beam";
        [JsonProperty("tickIntervalS")] public double TickIntervalS { get; set; }
        [JsonProperty("ramp")] public BeamRamp? Ramp { get; set; }
    }

    public sealed class FireModeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(FireMode).IsAssignableFrom(t);
        public override bool CanWrite => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JObject.Load(reader);
            var mode = (string?)token["mode"];
            FireMode target = mode switch
            {
                "instant" => new InstantFire(),
                "volley" => new VolleyFire(),
                "beam" => new BeamFire(),
                _ => throw new JsonSerializationException($"unknown FireMode mode '{mode}'"),
            };
            serializer.Populate(token.CreateReader(), target);
            return target;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    public sealed class Barrels
    {
        [JsonProperty("count")] public int Count { get; set; }
        [JsonProperty("independentTargets")] public bool IndependentTargets { get; set; }
    }

    public sealed class Projectile
    {
        [JsonProperty("speed")] public double Speed { get; set; }
        [JsonProperty("arcing")] public bool Arcing { get; set; }
        [JsonProperty("hp")] public double Hp { get; set; }
    }

    public sealed class Weapon
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("targets")] public List<TargetDomain> Targets { get; set; } = new List<TargetDomain>();
        [JsonProperty("range")] public double Range { get; set; }
        [JsonProperty("minRange")] public double MinRange { get; set; }
        [JsonProperty("interval")] public double Interval { get; set; }
        [JsonProperty("damage")] public ScaledValue? Damage { get; set; }
        [JsonProperty("splashRadius")] public double SplashRadius { get; set; }
        [JsonProperty("barrels")] public Barrels? Barrels { get; set; }
        [JsonProperty("fire")] public FireMode Fire { get; set; } = new InstantFire();
        [JsonProperty("projectile")] public Projectile? Projectile { get; set; }
        [JsonProperty("onImpact")] public List<Effect> OnImpact { get; set; } = new List<Effect>();
        [JsonProperty("onBeamTick")] public List<Effect> OnBeamTick { get; set; } = new List<Effect>();
    }

    // -----------------------------------------------------------------------
    // Abilities & Techs
    // -----------------------------------------------------------------------

    public sealed class AbilityArea
    {
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("filter")] public Filter Filter { get; set; } = new Filter();
    }

    public sealed class Ability
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("trigger")] public Trigger Trigger { get; set; } = new OnSpawnTrigger();
        [JsonProperty("area")] public AbilityArea? Area { get; set; }
        [JsonProperty("effects")] public List<Effect> Effects { get; set; } = new List<Effect>();
    }

    [JsonConverter(typeof(TechEffectConverter))]
    public abstract class TechEffect
    {
        [JsonProperty("kind")] public abstract string Kind { get; }
    }

    public sealed class StatModTech : TechEffect
    {
        public override string Kind => "statMod";
        [JsonProperty("mods")] public List<StatMod> Mods { get; set; } = new List<StatMod>();
    }

    public sealed class GrantAbilityTech : TechEffect
    {
        public override string Kind => "grantAbility";
        [JsonProperty("ability")] public Ability Ability { get; set; } = new Ability();
    }

    public sealed class WeaponPatch
    {
        [JsonProperty("range")] public double? Range { get; set; }
        [JsonProperty("minRange")] public double? MinRange { get; set; }
        [JsonProperty("interval")] public double? Interval { get; set; }
        [JsonProperty("damageAddPct")] public double? DamageAddPct { get; set; }
        [JsonProperty("splashRadius")] public double? SplashRadius { get; set; }
        [JsonProperty("addTargets")] public List<TargetDomain>? AddTargets { get; set; }
    }

    public sealed class ModifyWeaponTech : TechEffect
    {
        public override string Kind => "modifyWeapon";
        [JsonProperty("weaponId")] public string WeaponId { get; set; } = "";
        [JsonProperty("patch")] public WeaponPatch Patch { get; set; } = new WeaponPatch();
    }

    public sealed class GrantImmunityTech : TechEffect
    {
        public override string Kind => "grantImmunity";
        [JsonProperty("statusTag")] public string StatusTag { get; set; } = "";
    }

    public sealed class TechEffectConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(TechEffect).IsAssignableFrom(t);
        public override bool CanWrite => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JObject.Load(reader);
            var kind = (string?)token["kind"];
            TechEffect target = kind switch
            {
                "statMod" => new StatModTech(),
                "grantAbility" => new GrantAbilityTech(),
                "modifyWeapon" => new ModifyWeaponTech(),
                "grantImmunity" => new GrantImmunityTech(),
                _ => throw new JsonSerializationException($"unknown TechEffect kind '{kind}'"),
            };
            serializer.Populate(token.CreateReader(), target);
            return target;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    public sealed class Tech
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("cost")] public int Cost { get; set; }
        [JsonProperty("effects")] public List<TechEffect> Effects { get; set; } = new List<TechEffect>();
    }

    // -----------------------------------------------------------------------
    // Units
    // -----------------------------------------------------------------------

    public sealed class FormationOffset
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
    }

    public sealed class Footprint
    {
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }
    }

    public sealed class UnitCost
    {
        [JsonProperty("deployCost")] public int DeployCost { get; set; }
        [JsonProperty("unlockCost")] public int UnlockCost { get; set; }
    }

    public sealed class Placement
    {
        [JsonProperty("footprint")] public Footprint Footprint { get; set; } = new Footprint();
        [JsonProperty("domain")] public Domain Domain { get; set; }
    }

    public sealed class Squad
    {
        [JsonProperty("count")] public int Count { get; set; }
        [JsonProperty("xpToLevel")] public int XpToLevel { get; set; }
        [JsonProperty("formation")] public List<FormationOffset> Formation { get; set; } = new List<FormationOffset>();
    }

    public sealed class Member
    {
        [JsonProperty("hp")] public double Hp { get; set; }
        [JsonProperty("speed")] public double Speed { get; set; }
        [JsonProperty("flatBlock")] public double FlatBlock { get; set; }
        [JsonProperty("weapons")] public List<Weapon> Weapons { get; set; } = new List<Weapon>();
        [JsonProperty("abilities")] public List<Ability> Abilities { get; set; } = new List<Ability>();
    }

    public sealed class UnitDef
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("description")] public string Description { get; set; } = "";
        [JsonProperty("tier")] public int Tier { get; set; }
        [JsonProperty("cost")] public UnitCost Cost { get; set; } = new UnitCost();
        [JsonProperty("placement")] public Placement Placement { get; set; } = new Placement();
        [JsonProperty("squad")] public Squad Squad { get; set; } = new Squad();
        [JsonProperty("member")] public Member Member { get; set; } = new Member();
        [JsonProperty("techs")] public List<Tech> Techs { get; set; } = new List<Tech>();
    }

    // -----------------------------------------------------------------------
    // Statuses & Zones
    // -----------------------------------------------------------------------

    public sealed class StatusDot
    {
        [JsonProperty("amountPerSecond")] public double AmountPerSecond { get; set; }
    }

    public sealed class StatusFlags
    {
        [JsonProperty("techsDisabled")] public bool TechsDisabled { get; set; }
        [JsonProperty("untargetable")] public bool Untargetable { get; set; }
        [JsonProperty("stunned")] public bool Stunned { get; set; }
    }

    public sealed class Status
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("mods")] public List<StatMod> Mods { get; set; } = new List<StatMod>();
        [JsonProperty("dot")] public StatusDot? Dot { get; set; }
        [JsonProperty("flags")] public StatusFlags Flags { get; set; } = new StatusFlags();
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new List<string>();
    }

    public sealed class Zone
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("radius")] public double Radius { get; set; }
        [JsonProperty("reapplyIntervalS")] public double ReapplyIntervalS { get; set; }
        [JsonProperty("statusId")] public string StatusId { get; set; } = "";
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new List<string>();
    }

    public sealed class ContentPack
    {
        [JsonProperty("packId")] public string PackId { get; set; } = "";
        [JsonProperty("version")] public string Version { get; set; } = "";
        [JsonProperty("units")] public List<UnitDef> Units { get; set; } = new List<UnitDef>();
        [JsonProperty("statuses")] public List<Status> Statuses { get; set; } = new List<Status>();
        [JsonProperty("zones")] public List<Zone> Zones { get; set; } = new List<Zone>();
    }

    // -----------------------------------------------------------------------
    // Commanders & match rules
    // -----------------------------------------------------------------------

    [JsonConverter(typeof(CommanderAbilityConverter))]
    public abstract class CommanderAbility
    {
        [JsonProperty("kind")] public abstract string Kind { get; }
    }

    public sealed class CommanderStatMod : CommanderAbility
    {
        public override string Kind => "statMod";
        [JsonProperty("unitFilter")] public List<string> UnitFilter { get; set; } = new List<string>();
        [JsonProperty("mods")] public List<StatMod> Mods { get; set; } = new List<StatMod>();
    }

    public sealed class CommanderEconomyMod : CommanderAbility
    {
        public override string Kind => "economyMod";
        [JsonProperty("incomePerRoundAdd")] public int IncomePerRoundAdd { get; set; }
        [JsonProperty("deploySlotsAdd")] public int DeploySlotsAdd { get; set; }
        [JsonProperty("unlockSlotsAdd")] public int UnlockSlotsAdd { get; set; }
        [JsonProperty("startingIncomeAdd")] public int StartingIncomeAdd { get; set; }
    }

    public sealed class CommanderAbilityConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => typeof(CommanderAbility).IsAssignableFrom(t);
        public override bool CanWrite => false;
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var token = JObject.Load(reader);
            var kind = (string?)token["kind"];
            CommanderAbility target = kind switch
            {
                "statMod" => new CommanderStatMod(),
                "economyMod" => new CommanderEconomyMod(),
                _ => throw new JsonSerializationException($"unknown CommanderAbility kind '{kind}'"),
            };
            serializer.Populate(token.CreateReader(), target);
            return target;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => throw new NotImplementedException();
    }

    public sealed class Anchor
    {
        [JsonProperty("row")] public int Row { get; set; }
        [JsonProperty("col")] public int Col { get; set; }
    }

    public sealed class PlacedUnit
    {
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("anchor")] public Anchor Anchor { get; set; } = new Anchor();
        [JsonProperty("orientation")] public Orientation Orientation { get; set; }
    }

    public sealed class Commander
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("description")] public string Description { get; set; } = "";
        [JsonProperty("hp")] public int Hp { get; set; }
        [JsonProperty("startingUnits")] public List<PlacedUnit> StartingUnits { get; set; } = new List<PlacedUnit>();
        [JsonProperty("ability")] public List<CommanderAbility> Ability { get; set; } = new List<CommanderAbility>();
    }

    public sealed class Board
    {
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }
    }

    public sealed class Rect
    {
        [JsonProperty("row")] public int Row { get; set; }
        [JsonProperty("col")] public int Col { get; set; }
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }
    }

    public sealed class DeployZone
    {
        [JsonProperty("seat")] public int Seat { get; set; }
        [JsonProperty("rect")] public Rect Rect { get; set; } = new Rect();
        [JsonProperty("availableFromRound")] public int AvailableFromRound { get; set; } = 1;
    }

    public sealed class StartingBuilding
    {
        [JsonProperty("seat")] public int Seat { get; set; }
        [JsonProperty("unitId")] public string UnitId { get; set; } = "";
        [JsonProperty("anchor")] public Anchor Anchor { get; set; } = new Anchor();
        [JsonProperty("orientation")] public Orientation Orientation { get; set; }
    }

    public sealed class Income
    {
        [JsonProperty("perRoundIncrement")] public int PerRoundIncrement { get; set; }
        [JsonProperty("startingIncome")] public int StartingIncome { get; set; }
        [JsonProperty("carryOver")] public bool CarryOver { get; set; } = true;
    }

    public sealed class Timers
    {
        [JsonProperty("deploySeconds")] public int DeploySeconds { get; set; }
        [JsonProperty("battleSeconds")] public int BattleSeconds { get; set; }
        [JsonProperty("resultsHoldSeconds")] public int ResultsHoldSeconds { get; set; }
        [JsonProperty("commanderPickSeconds")] public int CommanderPickSeconds { get; set; }
    }

    public sealed class Leveling
    {
        [JsonProperty("hpFactorPerLevel")] public double HpFactorPerLevel { get; set; }
        [JsonProperty("atkFactorPerLevel")] public double AtkFactorPerLevel { get; set; }
        [JsonProperty("upgradeCostFraction")] public double UpgradeCostFraction { get; set; }
    }

    public sealed class MatchRules
    {
        [JsonProperty("board")] public Board Board { get; set; } = new Board();
        [JsonProperty("deployZones")] public List<DeployZone> DeployZones { get; set; } = new List<DeployZone>();
        [JsonProperty("income")] public Income Income { get; set; } = new Income();
        [JsonProperty("deploysPerRound")] public int DeploysPerRound { get; set; }
        [JsonProperty("unlocksPerRound")] public int UnlocksPerRound { get; set; }
        [JsonProperty("timers")] public Timers Timers { get; set; } = new Timers();
        [JsonProperty("leveling")] public Leveling Leveling { get; set; } = new Leveling();
        [JsonProperty("techPriceEscalation")] public int TechPriceEscalation { get; set; }
        [JsonProperty("commandersOffered")] public int CommandersOffered { get; set; } = 3;
        [JsonProperty("startingBuildings")] public List<StartingBuilding> StartingBuildings { get; set; } = new List<StartingBuilding>();
        [JsonProperty("commanders")] public List<Commander> Commanders { get; set; } = new List<Commander>();
    }

    // -----------------------------------------------------------------------
    // The built catalog
    // -----------------------------------------------------------------------

    public sealed class Catalog
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonProperty("packs")] public List<ContentPack> Packs { get; set; } = new List<ContentPack>();
        [JsonProperty("matchRules")] public MatchRules MatchRules { get; set; } = new MatchRules();
    }
}
