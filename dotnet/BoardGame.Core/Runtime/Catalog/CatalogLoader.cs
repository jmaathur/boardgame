using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BoardGame.Core.Generated;
using Newtonsoft.Json;

namespace BoardGame.Core.Catalog
{
    /// <summary>
    /// Parses the built catalog JSON (the exact dist bytes, delivered on
    /// `welcome` or read from StreamingAssets) into typed DTOs with indexed
    /// lookups. The schemaVersion gate is a hard failure — a version mismatch
    /// means the shapes changed and the runtime must not silently reinterpret.
    /// The hash is computed over the bytes AS RECEIVED (canonicalization happens
    /// exactly once, in the TS build).
    /// </summary>
    public sealed class LoadedCatalog
    {
        public Generated.Catalog Catalog { get; }
        public string Hash { get; }
        public MatchRules MatchRules => Catalog.MatchRules;

        private readonly Dictionary<string, UnitDef> _unitById;
        private readonly Dictionary<string, Commander> _commanderById;
        private readonly Dictionary<string, Status> _statusById;

        internal LoadedCatalog(Generated.Catalog catalog, string hash)
        {
            Catalog = catalog;
            Hash = hash;
            _unitById = new Dictionary<string, UnitDef>();
            _commanderById = new Dictionary<string, Commander>();
            _statusById = new Dictionary<string, Status>();
            foreach (var pack in catalog.Packs)
            {
                foreach (var unit in pack.Units) _unitById[unit.Id] = unit;
                foreach (var status in pack.Statuses) _statusById[status.Id] = status;
            }
            foreach (var commander in catalog.MatchRules.Commanders)
                _commanderById[commander.Id] = commander;
        }

        public bool TryGetUnit(string id, out UnitDef unit) => _unitById.TryGetValue(id, out unit!);
        public UnitDef GetUnit(string id) => _unitById[id];
        public bool HasUnit(string id) => _unitById.ContainsKey(id);
        public IReadOnlyCollection<UnitDef> Units => _unitById.Values;

        public bool TryGetCommander(string id, out Commander commander) => _commanderById.TryGetValue(id, out commander!);
        public bool TryGetStatus(string id, out Status status) => _statusById.TryGetValue(id, out status!);
        public bool HasStatus(string id) => _statusById.ContainsKey(id);
    }

    public static class CatalogLoader
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>
        /// Parse catalog JSON. Throws on a schemaVersion mismatch or malformed
        /// JSON. The hash is sha256 of the exact input bytes (hex).
        /// </summary>
        public static LoadedCatalog Load(string canonicalJson)
        {
            var catalog = JsonConvert.DeserializeObject<Generated.Catalog>(canonicalJson, Settings)
                ?? throw new JsonSerializationException("catalog JSON deserialized to null");
            if (catalog.SchemaVersion != CatalogSchema.Version)
            {
                throw new InvalidOperationException(
                    $"catalog schemaVersion {catalog.SchemaVersion} != runtime {CatalogSchema.Version} — shapes changed");
            }
            return new LoadedCatalog(catalog, HashOf(canonicalJson));
        }

        /// <summary>
        /// Load and verify the hash matches an expected value (e.g. the one the
        /// server sent alongside the bytes). Throws on mismatch.
        /// </summary>
        public static LoadedCatalog LoadVerified(string canonicalJson, string expectedHash)
        {
            var loaded = Load(canonicalJson);
            if (!string.Equals(loaded.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"catalog hash {loaded.Hash} != expected {expectedHash}");
            }
            return loaded;
        }

        public static string HashOf(string canonicalJson)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalJson));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
