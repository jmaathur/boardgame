// Each integration test boots a full ASP.NET Core host; running them in parallel
// (especially alongside the Core.Tests assembly) causes startup-latency timeouts.
// Serialize them within this assembly for stable timing.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
