using System;
using System.Collections.Generic;
using BoardGame.Core.Catalog;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Sim
{
    /// <summary>
    /// Helpers to assemble battle armies — from parsed lineup strings (the
    /// balance harness) or from match blueprints (the server). Auto-lays squads
    /// out in the seat's half so the balance harness needs only "unit x count".
    /// </summary>
    public static class BattleSetup
    {
        /// <summary>
        /// Build an army for a seat from (unitId, count) pairs, tiling squads
        /// across the seat's half so nothing overlaps or spills off-board. Seat 1
        /// is laid out as an EXACT mirror of the seat-0 layout reflected across
        /// the midline, so a lineup fought against itself is perfectly symmetric
        /// (identical inputs → a draw, not a first-mover win).
        /// </summary>
        public static ArmyBlueprint FromLineup(LoadedCatalog catalog, int seat, IEnumerable<(string unitId, int count)> entries)
        {
            var board = catalog.MatchRules.Board;
            int mid = board.H / 2;

            // Canonical seat-0 layout: tile squads in cols [mid-8, mid), the
            // front rank abutting the midline, wrapping into row bands.
            int colBase = Math.Max(0, mid - 8);
            int colLimit = mid;
            var canonical = new List<SquadBlueprint>();
            int row = 0, col = colBase, rowMax = 0;
            foreach (var (unitId, count) in entries)
            {
                if (!catalog.TryGetUnit(unitId, out var unit)) continue;
                // Footprint w extends along the ROW axis (world-X), h along the
                // COL axis (world-Z). Tile along cols, wrap into row bands.
                var fp = unit.Placement.Footprint;
                for (int i = 0; i < count; i++)
                {
                    if (col + fp.H > colLimit)
                    {
                        col = colBase;
                        row += rowMax + 1;
                        rowMax = 0;
                    }
                    if (row + fp.W > board.W) break; // out of room
                    canonical.Add(new SquadBlueprint
                    {
                        UnitId = unitId,
                        AnchorRow = row,
                        AnchorCol = col,
                        Orientation = Orientation.North,
                        Level = 1,
                        Invested = unit.Cost.DeployCost,
                        CardId = $"{unitId}-{i}",
                    });
                    col += fp.H + 1;
                    rowMax = Math.Max(rowMax, fp.W);
                }
            }

            var army = new ArmyBlueprint { Seat = seat };
            if (seat == 0)
            {
                army.Squads.AddRange(canonical);
                return army;
            }
            // Seat 1: reflect each squad's col across the midline. A squad
            // occupying cols [c, c+h) maps to [2*mid - (c+h), 2*mid - c).
            foreach (var bp in canonical)
            {
                if (!catalog.TryGetUnit(bp.UnitId, out var unit)) continue;
                int h = unit.Placement.Footprint.H;
                army.Squads.Add(new SquadBlueprint
                {
                    UnitId = bp.UnitId,
                    AnchorRow = bp.AnchorRow,
                    AnchorCol = 2 * mid - (bp.AnchorCol + h),
                    Orientation = Orientation.North,
                    Level = bp.Level,
                    Invested = bp.Invested,
                    CardId = bp.CardId,
                });
            }
            return army;
        }
    }
}
