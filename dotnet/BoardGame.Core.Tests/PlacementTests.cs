using System.Collections.Generic;
using System.Linq;
using BoardGame.Core.Catalog;
using BoardGame.Core.Generated;
using BoardGame.Core.Match;
using Xunit;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// Footprint geometry + placement validation — the C# mirror of the Bun
    /// room's placement property tests, now on the shared engine.
    /// </summary>
    public class PlacementTests
    {
        private static LoadedCatalog Catalog() => CatalogLoader.Load(CatalogTestData.CanonicalJson());

        private static PlacementResult Place(LoadedCatalog cat, string unit, int row, int col,
            IEnumerable<OccupiedFootprint>? occupied = null, int seat = 0, Orientation o = Orientation.North)
            => PlacementRules.Validate(cat, unit, seat, row, col, o, occupied ?? Enumerable.Empty<OccupiedFootprint>());

        [Fact]
        public void PlacesAUnitInItsOwnHalf()
        {
            var cat = Catalog();
            // seat 0 owns cols 0..23; archer 4x2 at (10,4) => rows 10..13, cols 4..5.
            Assert.True(Place(cat, "archer", 10, 4).Ok);
        }

        [Fact]
        public void RejectsAnUnknownUnit()
        {
            var r = Place(Catalog(), "dragonlord", 0, 0);
            Assert.False(r.Ok);
            Assert.Equal(PlacementError.UnknownUnit, r.Error);
        }

        [Fact]
        public void RejectsAFootprintOffTheBoard()
        {
            var cat = Catalog();
            // cathedral 4x4 at row 30 => rows 30..33 > 31.
            var r = Place(cat, "cathedral", 30, 0);
            Assert.False(r.Ok);
            Assert.Equal(PlacementError.OutOfBounds, r.Error);
        }

        [Fact]
        public void RejectsPlacementOutsideTheOwnHalf()
        {
            var cat = Catalog();
            // seat 0 owns cols 0..23; placing at col 30 is in seat 1's half.
            var r = Place(cat, "archer", 0, 30);
            Assert.False(r.Ok);
            Assert.Equal(PlacementError.OutsideOwnHalf, r.Error);
        }

        [Fact]
        public void BuildingsAreExemptFromTheOwnHalfCheck()
        {
            var cat = Catalog();
            // cathedral is a building; placing it deep (col 40) is allowed by the
            // own-half rule (it fails only if off-board).
            var r = Place(cat, "cathedral", 0, 40);
            Assert.True(r.Ok);
        }

        [Fact]
        public void RejectsOverlappingFootprints()
        {
            var cat = Catalog();
            var first = PlacementRules.TilesFor(cat, "archer", 5, 5, Orientation.North); // rows 5..8, cols 5..6
            var occupied = new[] { new OccupiedFootprint(first) };
            // whelp 4x3 at (6,5) => rows 6..9, cols 5..7 — overlaps.
            var r = Place(cat, "whelp", 6, 5, occupied);
            Assert.False(r.Ok);
            Assert.Equal(PlacementError.Overlap, r.Error);
        }

        [Fact]
        public void AllowsNonOverlappingFootprints()
        {
            var cat = Catalog();
            var first = PlacementRules.TilesFor(cat, "archer", 0, 0, Orientation.North); // rows 0..3, cols 0..1
            var occupied = new[] { new OccupiedFootprint(first) };
            // archer at (0,2) => rows 0..3, cols 2..3 — abuts, no overlap.
            Assert.True(Place(cat, "archer", 0, 2, occupied).Ok);
        }

        [Fact]
        public void OrientationSwapsFootprintDimensions()
        {
            var cat = Catalog();
            var archer = cat.GetUnit("archer"); // 4x2
            var north = Footprints.OrientedSize(archer.Placement.Footprint, Orientation.North);
            var east = Footprints.OrientedSize(archer.Placement.Footprint, Orientation.East);
            Assert.Equal((4, 2), north);
            Assert.Equal((2, 4), east);
        }

        [Fact]
        public void OrientationRotatesFormationOffsets()
        {
            var cat = Catalog();
            var archer = cat.GetUnit("archer");
            var north = Footprints.OrientedFormation(archer, Orientation.North);
            var east = Footprints.OrientedFormation(archer, Orientation.East);
            Assert.Equal(archer.Squad.Formation.Count, east.Count);
            // 90° CW: (x,z) -> (z,-x). Check the first member.
            var f0 = archer.Squad.Formation[0];
            Assert.Equal(f0.Z, east[0].X, 3);
            Assert.Equal(-f0.X, east[0].Z, 3);
            // North is identity.
            Assert.Equal(f0.X, north[0].X, 3);
        }

        [Fact]
        public void FormationLengthMatchesSquadCountForEveryUnit()
        {
            var cat = Catalog();
            foreach (var unit in cat.Units)
            {
                Assert.Equal(unit.Squad.Count, unit.Squad.Formation.Count);
            }
        }
    }
}
