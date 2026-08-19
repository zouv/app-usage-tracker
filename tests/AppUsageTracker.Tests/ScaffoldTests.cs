namespace AppUsageTracker.Tests;

public class ScaffoldTests
{
    [Fact]
    public void TestAssemblyLoads()
    {
        Assert.NotNull(typeof(App).Assembly);
    }
}
