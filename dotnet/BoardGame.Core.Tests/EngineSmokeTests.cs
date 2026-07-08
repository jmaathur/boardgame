using BoardGame.Core;
using Xunit;

namespace BoardGame.Core.Tests
{
    /// <summary>
    /// Placeholder proving the test harness wires up against the engine
    /// assembly. Real golden-scenario, determinism, and placement-property
    /// tests replace this from M2 onward.
    /// </summary>
    public class EngineSmokeTests
    {
        [Fact]
        public void SchemaVersionIsPositive()
        {
            Assert.True(EngineInfo.SchemaVersion > 0);
        }
    }
}
