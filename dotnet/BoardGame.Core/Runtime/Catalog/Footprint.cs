using System.Collections.Generic;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Catalog
{
    /// <summary>An inclusive axis-aligned tile rectangle on the board.</summary>
    public readonly struct TileRect
    {
        public readonly int RowStart;
        public readonly int RowEnd;
        public readonly int ColStart;
        public readonly int ColEnd;

        public TileRect(int rowStart, int rowEnd, int colStart, int colEnd)
        {
            RowStart = rowStart;
            RowEnd = rowEnd;
            ColStart = colStart;
            ColEnd = colEnd;
        }

        public bool Overlaps(TileRect o) =>
            RowStart <= o.RowEnd && o.RowStart <= RowEnd &&
            ColStart <= o.ColEnd && o.ColStart <= ColEnd;
    }

    /// <summary>
    /// Footprint + formation geometry, shared by placement validation and the
    /// battle sim so both agree byte-for-byte. Orientation rotates the footprint
    /// (swap w/h) and each formation offset (x,z)→(z,−x) per 90° step.
    /// </summary>
    public static class Footprints
    {
        /// <summary>Footprint w/h after applying an orientation.</summary>
        public static (int W, int H) OrientedSize(Footprint fp, Orientation o)
        {
            switch (o)
            {
                case Orientation.East:
                case Orientation.West:
                    return (fp.H, fp.W);
                default:
                    return (fp.W, fp.H);
            }
        }

        /// <summary>
        /// The tiles a unit occupies when anchored at (row, col) — the anchor is
        /// the min corner; w extends along the row axis (world-X), h along the
        /// col axis (world-Z).
        /// </summary>
        public static TileRect Tiles(UnitDef unit, int row, int col, Orientation o = Orientation.North)
        {
            var (w, h) = OrientedSize(unit.Placement.Footprint, o);
            return new TileRect(row, row + w - 1, col, col + h - 1);
        }

        /// <summary>Does the footprint at (row,col) fit inside a w×h board?</summary>
        public static bool FitsBoard(UnitDef unit, int row, int col, int boardW, int boardH, Orientation o = Orientation.North)
        {
            var t = Tiles(unit, row, col, o);
            return t.RowStart >= 0 && t.ColStart >= 0 && t.RowEnd <= boardW - 1 && t.ColEnd <= boardH - 1;
        }

        /// <summary>
        /// Center-relative formation offsets rotated for an orientation. Each 90°
        /// clockwise step maps (x,z)→(z,−x); the footprint size swaps in lockstep
        /// (see OrientedSize).
        /// </summary>
        public static List<(double X, double Z)> OrientedFormation(UnitDef unit, Orientation o)
        {
            var steps = o switch
            {
                Orientation.East => 1,
                Orientation.South => 2,
                Orientation.West => 3,
                _ => 0,
            };
            var result = new List<(double, double)>(unit.Squad.Formation.Count);
            foreach (var off in unit.Squad.Formation)
            {
                double x = off.X, z = off.Z;
                for (int i = 0; i < steps; i++)
                {
                    var nx = z;
                    var nz = -x;
                    x = nx;
                    z = nz;
                }
                result.Add((x, z));
            }
            return result;
        }
    }
}
