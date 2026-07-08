using System.Collections.Generic;
using BoardGame.Core.Catalog;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Match
{
    public enum PlacementError
    {
        None,
        UnknownUnit,
        OutOfBounds,
        OutsideOwnHalf,
        Overlap,
    }

    public readonly struct PlacementResult
    {
        public readonly bool Ok;
        public readonly PlacementError Error;
        public readonly string Message;

        private PlacementResult(bool ok, PlacementError error, string message)
        {
            Ok = ok;
            Error = error;
            Message = message;
        }

        public static readonly PlacementResult Success = new PlacementResult(true, PlacementError.None, "");
        public static PlacementResult Fail(PlacementError e, string msg) => new PlacementResult(false, e, msg);
    }

    /// <summary>An already-validated placement occupying board tiles.</summary>
    public readonly struct OccupiedFootprint
    {
        public readonly TileRect Tiles;
        public OccupiedFootprint(TileRect tiles) { Tiles = tiles; }
    }

    /// <summary>
    /// Placement validation — the C# port of the Bun Room's ~50 lines, now the
    /// shared authority for both servers. Validates catalog membership, board
    /// bounds, own-half (deploy zone) containment, and footprint overlap.
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>
        /// Validate placing <paramref name="unitId"/> at (row, col) for a seat,
        /// against the board, that seat's deploy zones, and the already-occupied
        /// footprints. Buildings skip the own-half check (they use fixed
        /// starting placements).
        /// </summary>
        public static PlacementResult Validate(
            LoadedCatalog catalog,
            string unitId,
            int seat,
            int row,
            int col,
            Orientation orientation,
            IEnumerable<OccupiedFootprint> occupied)
        {
            if (!catalog.TryGetUnit(unitId, out var unit))
                return PlacementResult.Fail(PlacementError.UnknownUnit, $"no unit \"{unitId}\" in the catalog");

            var board = catalog.MatchRules.Board;
            if (!Footprints.FitsBoard(unit, row, col, board.W, board.H, orientation))
                return PlacementResult.Fail(PlacementError.OutOfBounds,
                    $"{unitId} at ({row},{col}) does not fit the {board.W}x{board.H} board");

            var tiles = Footprints.Tiles(unit, row, col, orientation);

            if (unit.Placement.Domain != Domain.Building && !WithinSeatZones(catalog, seat, tiles))
                return PlacementResult.Fail(PlacementError.OutsideOwnHalf,
                    $"{unitId} at ({row},{col}) is outside seat {seat}'s deploy zone");

            foreach (var o in occupied)
            {
                if (tiles.Overlaps(o.Tiles))
                    return PlacementResult.Fail(PlacementError.Overlap,
                        $"footprint at ({row},{col}) overlaps an existing unit");
            }

            return PlacementResult.Success;
        }

        /// <summary>The tile rect a validated placement occupies.</summary>
        public static TileRect TilesFor(LoadedCatalog catalog, string unitId, int row, int col, Orientation orientation)
            => Footprints.Tiles(catalog.GetUnit(unitId), row, col, orientation);

        private static bool WithinSeatZones(LoadedCatalog catalog, int seat, TileRect tiles)
        {
            foreach (var zone in catalog.MatchRules.DeployZones)
            {
                if (zone.Seat != seat) continue;
                var r = zone.Rect;
                var zoneRect = new TileRect(r.Row, r.Row + r.W - 1, r.Col, r.Col + r.H - 1);
                if (tiles.RowStart >= zoneRect.RowStart && tiles.RowEnd <= zoneRect.RowEnd &&
                    tiles.ColStart >= zoneRect.ColStart && tiles.ColEnd <= zoneRect.ColEnd)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
