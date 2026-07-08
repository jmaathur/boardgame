namespace BoardGame.Core.Sim
{
    /// <summary>
    /// Deterministic seeded RNG (xorshift128). The sim consumes it in a fixed
    /// phase order so the same seed + inputs produce byte-identical event logs.
    /// Never use System.Random anywhere in the sim — its sequence is not
    /// guaranteed stable across runtimes and would break replay/determinism.
    /// </summary>
    public sealed class XorShiftRng
    {
        private uint _x, _y, _z, _w;

        public XorShiftRng(uint seed)
        {
            // Seed all four lanes from the single seed via a splitmix-like spread
            // so a zero seed still produces a non-degenerate state.
            _x = seed == 0 ? 0x9E3779B9u : seed;
            _y = _x * 1812433253u + 1u;
            _z = _y * 1812433253u + 1u;
            _w = _z * 1812433253u + 1u;
        }

        public uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            _w = _w ^ (_w >> 19) ^ (t ^ (t >> 8));
            return _w;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextUInt() >> 8) * (1.0 / (1u << 24));

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <summary>Symmetric jitter in [-spread, +spread].</summary>
        public double NextSpread(double spread) => (NextDouble() * 2.0 - 1.0) * spread;
    }
}
