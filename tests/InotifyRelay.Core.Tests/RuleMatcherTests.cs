using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class RuleMatcherTests
{
    private static RuleSnapshot MakeRule(FileEventType mask, params (string root, string? glob)[] sources)
        => new(Guid.NewGuid(), "rule", true, mask, 0, 0,
            sources.Select(s => new SourceSnapshot(s.root, s.glob, true)).ToList(),
            new List<TargetBindingSnapshot>());

    [Fact]
    public void Matches_when_path_under_source_root()
    {
        var rule = MakeRule(FileEventType.Created, ("/watch/movies", null));
        var change = new FileSystemChange("/watch/movies/x.mkv", FileEventType.Created, false,
            DateTimeOffset.UtcNow, "/watch/movies");
        Assert.Single(RuleMatcher.Match(change, new[] { rule }));
    }

    [Fact]
    public void No_match_when_path_outside_source()
    {
        var rule = MakeRule(FileEventType.Created, ("/watch/movies", null));
        var change = new FileSystemChange("/watch/tv/y.mkv", FileEventType.Created, false,
            DateTimeOffset.UtcNow, "/watch/tv");
        Assert.Empty(RuleMatcher.Match(change, new[] { rule }));
    }

    [Fact]
    public void No_match_when_event_type_not_in_mask()
    {
        var rule = MakeRule(FileEventType.Created, ("/x", null));
        var change = new FileSystemChange("/x/a", FileEventType.Modified, false,
            DateTimeOffset.UtcNow, "/x");
        Assert.Empty(RuleMatcher.Match(change, new[] { rule }));
    }

    [Fact]
    public void Glob_pattern_filters_extension()
    {
        var rule = MakeRule(FileEventType.Created, ("/x", "**/*.mkv"));
        var ok = new FileSystemChange("/x/a/b.mkv", FileEventType.Created, false, DateTimeOffset.UtcNow, "/x");
        var no = new FileSystemChange("/x/a/b.srt", FileEventType.Created, false, DateTimeOffset.UtcNow, "/x");
        Assert.Single(RuleMatcher.Match(ok, new[] { rule }));
        Assert.Empty(RuleMatcher.Match(no, new[] { rule }));
    }

    [Fact]
    public void Disabled_rule_does_not_match()
    {
        var rule = MakeRule(FileEventType.Created, ("/x", null)) with { Enabled = false };
        var change = new FileSystemChange("/x/a", FileEventType.Created, false, DateTimeOffset.UtcNow, "/x");
        Assert.Empty(RuleMatcher.Match(change, new[] { rule }));
    }
}
