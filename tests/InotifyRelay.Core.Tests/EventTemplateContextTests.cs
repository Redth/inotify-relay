using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Templating;

namespace InotifyRelay.Core.Tests;

public class EventTemplateContextTests
{
    static readonly TemplateFilterRegistry Filters = TemplateFilterRegistry.CreateDefault();

    private static FileSystemChange SampleChange() => new(
        Path: "/watch/movies/Inception/a.mkv",
        EventType: FileEventType.ClosedWrite,
        IsDirectory: false,
        Timestamp: new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero),
        SourceRoot: "/watch/movies");

    [Fact]
    public void Without_mappings_path_vars_are_raw()
    {
        var ctx = EventTemplateContext.Build(SampleChange(), "r", "r-id");
        Assert.Equal("/watch/movies/Inception/a.mkv", Template.Parse("{path}").Render(ctx, Filters));
        Assert.Equal("/watch/movies", Template.Parse("{sourceRoot}").Render(ctx, Filters));
        Assert.Equal("Inception/a.mkv", Template.Parse("{relativePath}").Render(ctx, Filters));
    }

    [Fact]
    public void With_mappings_path_directory_and_sourceRoot_are_rewritten()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        var ctx = EventTemplateContext.Build(SampleChange(), "r", "r-id", maps);

        Assert.Equal("/data/movies/Inception/a.mkv", Template.Parse("{path}").Render(ctx, Filters));
        Assert.Equal("/data/movies/Inception", Template.Parse("{directory}").Render(ctx, Filters));
        Assert.Equal("/data/movies", Template.Parse("{sourceRoot}").Render(ctx, Filters));
    }

    [Fact]
    public void With_mappings_source_dot_vars_keep_originals()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        var ctx = EventTemplateContext.Build(SampleChange(), "r", "r-id", maps);

        Assert.Equal("/watch/movies/Inception/a.mkv", Template.Parse("{source.path}").Render(ctx, Filters));
        Assert.Equal("/watch/movies/Inception", Template.Parse("{source.directory}").Render(ctx, Filters));
        Assert.Equal("/watch/movies", Template.Parse("{source.sourceRoot}").Render(ctx, Filters));
    }

    [Fact]
    public void RelativePath_is_invariant_under_mapping()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        var ctx = EventTemplateContext.Build(SampleChange(), "r", "r-id", maps);
        Assert.Equal("Inception/a.mkv", Template.Parse("{relativePath}").Render(ctx, Filters));
        Assert.Equal("Inception", Template.Parse("{relativeDirectory}").Render(ctx, Filters));
    }
}
