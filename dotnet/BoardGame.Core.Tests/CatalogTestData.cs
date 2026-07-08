using System;
using System.IO;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// Locates the real committed catalog (core/catalog/dist/catalog.json) so
    /// tests exercise the actual shipped bytes — the same conformance the Bun
    /// server gets. Walks up from the test binary to the repo root.
    /// </summary>
    public static class CatalogTestData
    {
        private static string? _cached;

        public static string CanonicalJson()
        {
            if (_cached != null) return _cached;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "core", "catalog", "dist", "catalog.json");
                if (File.Exists(candidate))
                {
                    _cached = File.ReadAllText(candidate);
                    return _cached;
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException(
                "could not locate core/catalog/dist/catalog.json above " + AppContext.BaseDirectory);
        }

        public static string ExpectedHash()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "core", "catalog", "dist", "catalog.hash");
                if (File.Exists(candidate)) return File.ReadAllText(candidate).Trim();
                dir = dir.Parent;
            }
            throw new FileNotFoundException("could not locate core/catalog/dist/catalog.hash");
        }
    }
}
