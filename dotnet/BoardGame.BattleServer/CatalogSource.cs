using System;
using System.IO;
using BoardGame.Core.Catalog;

namespace BoardGame.BattleServer
{
    /// <summary>
    /// Loads the built catalog for the server. Prefers the BOARDGAME_CATALOG env
    /// path; otherwise walks up from the binary to the committed
    /// core/catalog/dist/catalog.json (dev/CI). Deployments set the env var to a
    /// catalog shipped alongside the binary.
    /// </summary>
    public static class CatalogSource
    {
        public static (LoadedCatalog catalog, string canonicalJson) Load()
        {
            var path = Environment.GetEnvironmentVariable("BOARDGAME_CATALOG");
            if (string.IsNullOrEmpty(path)) path = FindCommitted();
            if (path == null || !File.Exists(path))
                throw new FileNotFoundException("could not locate catalog.json; set BOARDGAME_CATALOG");
            var json = File.ReadAllText(path);
            return (CatalogLoader.Load(json), json);
        }

        private static string? FindCommitted()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "core", "catalog", "dist", "catalog.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
