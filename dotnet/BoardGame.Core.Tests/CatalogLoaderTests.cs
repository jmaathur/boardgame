using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Generated;
using Newtonsoft.Json;
using Xunit;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// Conformance + drift guard: the hand-maintained DTOs must faithfully load
    /// the real committed catalog. If the JSON shapes and the C# DTOs drift
    /// apart, these fail — the same guarantee the plan wanted from a codegen
    /// drift test, achieved by round-tripping the actual shipped bytes.
    /// </summary>
    public class CatalogLoaderTests
    {
        [Fact]
        public void LoadsTheRealCatalogWithExpectedRoster()
        {
            var loaded = CatalogLoader.Load(CatalogTestData.CanonicalJson());

            Assert.Equal(CatalogSchema.Version, loaded.Catalog.SchemaVersion);
            // 10 units in the base pack (see core/catalog/data/packs/base.json).
            Assert.Equal(10, loaded.Units.Count);
            Assert.True(loaded.HasUnit("footman"));
            Assert.True(loaded.HasUnit("cathedral"));
            Assert.True(loaded.HasUnit("warBanner"));

            // Board is the decided 32x48.
            Assert.Equal(32, loaded.MatchRules.Board.W);
            Assert.Equal(48, loaded.MatchRules.Board.H);

            // 3 commanders offered.
            Assert.Equal(3, loaded.MatchRules.Commanders.Count);
        }

        [Fact]
        public void HashMatchesTheCommittedHash()
        {
            var loaded = CatalogLoader.Load(CatalogTestData.CanonicalJson());
            Assert.Equal(CatalogTestData.ExpectedHash(), loaded.Hash);
        }

        [Fact]
        public void LoadVerifiedThrowsOnHashMismatch()
        {
            Assert.ThrowsAny<System.Exception>(() =>
                CatalogLoader.LoadVerified(CatalogTestData.CanonicalJson(), "deadbeef"));
        }

        [Fact]
        public void RejectsAWrongSchemaVersion()
        {
            var json = CatalogTestData.CanonicalJson().Replace(
                "\"schemaVersion\":1", "\"schemaVersion\":999");
            // Only run the assertion if the replace actually changed something.
            Assert.Contains("\"schemaVersion\":999", json);
            Assert.ThrowsAny<System.Exception>(() => CatalogLoader.Load(json));
        }

        [Fact]
        public void DeserializesDiscriminatedUnionsCorrectly()
        {
            var loaded = CatalogLoader.Load(CatalogTestData.CanonicalJson());

            // ballista's weapon is a volley with an areaDamage onImpact effect.
            var ballista = loaded.GetUnit("ballista");
            var weapon = ballista.Member.Weapons.Single();
            Assert.IsType<VolleyFire>(weapon.Fire);
            var impact = Assert.Single(weapon.OnImpact);
            var area = Assert.IsType<AreaDamageEffect>(impact);
            Assert.True(area.Amount.At(1) > 0);

            // warBanner has an aura ability applying a status.
            var banner = loaded.GetUnit("warBanner");
            var ability = banner.Member.Abilities.Single();
            Assert.IsType<AuraTrigger>(ability.Trigger);
            Assert.IsType<ApplyStatusEffect>(ability.Effects.Single());

            // archer has techs, one of which is a modifyWeapon patch.
            var archer = loaded.GetUnit("archer");
            Assert.NotEmpty(archer.Techs);
            Assert.Contains(archer.Techs.SelectMany(t => t.Effects),
                e => e is ModifyWeaponTech);
        }

        [Fact]
        public void ScaledValueReadsBothForms()
        {
            // flat number
            var flat = JsonConvert.DeserializeObject<ScaledValue>("5");
            Assert.Equal(5, flat.At(1));
            Assert.Equal(5, flat.At(3));

            // {base, perLevel}
            var scaled = JsonConvert.DeserializeObject<ScaledValue>(
                "{\"base\":10,\"perLevel\":2}");
            Assert.Equal(10, scaled.At(1));
            Assert.Equal(14, scaled.At(3));
        }

        [Fact]
        public void CommanderAbilitiesDeserialize()
        {
            var loaded = CatalogLoader.Load(CatalogTestData.CanonicalJson());
            var warlord = loaded.MatchRules.Commanders.Single(c => c.Id == "warlord");
            var econ = Assert.IsType<CommanderEconomyMod>(warlord.Ability.Single());
            Assert.Equal(1, econ.DeploySlotsAdd);

            var zealot = loaded.MatchRules.Commanders.Single(c => c.Id == "zealot");
            Assert.IsType<CommanderStatMod>(zealot.Ability.Single());
        }
    }
}
