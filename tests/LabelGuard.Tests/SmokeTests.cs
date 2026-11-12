using LabelGuard.Core;

namespace LabelGuard.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssembly_IsReachable()
    {
        Assert.Equal("LabelGuard", AppInfo.Name);
    }
}
