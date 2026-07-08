namespace BoardGame.Core
{
    /// <summary>
    /// Marker for the engine assembly. Real modules (Catalog, Match, Sim,
    /// Events, Xp) land in later milestones; this keeps the "empty engine"
    /// building and gives the drift-guarded codegen a namespace to emit into.
    /// </summary>
    public static class EngineInfo
    {
        /// <summary>
        /// Hand-bumped when the catalog/protocol wire shapes change. Mirrors
        /// the <c>schemaVersion</c> in core/types; the catalog loader gates on
        /// it (a mismatch is a hard load error, not a silent reinterpret).
        /// </summary>
        public const int SchemaVersion = 1;
    }
}
