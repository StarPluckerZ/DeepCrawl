using DeepCrawl.Infrastructure.Plugins;
using Xunit;

namespace DeepCrawl.Infrastructure.Tests;

public class PluginFinderTests
{
    [Fact]
    public void Finds_Plugin_In_Current_Assembly()
    {
        var dir = Path.GetDirectoryName(typeof(PluginFinderTests).Assembly.Location)!;

        var finder = new PluginFinder();
        var plugins = finder.FindPlugins(dir).ToList();

        Assert.Contains(plugins, p => p is TestPlugin);
    }

    [Fact]
    public void Nonexistent_Directory_Returns_Empty()
    {
        var finder = new PluginFinder();
        Assert.Empty(finder.FindPlugins(@"X:\nonexistent\path"));
    }
}
