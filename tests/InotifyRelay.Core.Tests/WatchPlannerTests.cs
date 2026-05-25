using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class WatchPlannerTests
{
    private static RuleSnapshot MakeRule(bool enabled, params (string path, bool recursive)[] sources)
        => new(Guid.NewGuid(), "r", enabled, FileEventType.All, 0, 0,
            sources.Select(s => new SourceSnapshot(s.path, null, s.recursive)).ToList(),
            new List<TargetBindingSnapshot>());

    [Fact]
    public void Single_recursive_source_passes_through()
    {
        var plan = WatchPlanner.Plan(new[] { MakeRule(true, ("/movies", true)) });
        Assert.Single(plan);
        Assert.Equal(new WatchPlanEntry("/movies", true), plan[0]);
    }

    [Fact]
    public void Recursive_ancestor_subsumes_descendant_recursive()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies", true)),
            MakeRule(true, ("/movies/scifi", true)),
        });
        Assert.Single(plan);
        Assert.Equal("/movies", plan[0].Path);
        Assert.True(plan[0].Recursive);
    }

    [Fact]
    public void Recursive_ancestor_subsumes_descendant_nonrecursive()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies", true)),
            MakeRule(true, ("/movies/scifi", false)),
        });
        Assert.Single(plan);
        Assert.Equal(new WatchPlanEntry("/movies", true), plan[0]);
    }

    [Fact]
    public void Nonrecursive_kept_when_no_recursive_ancestor()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies", false)),
            MakeRule(true, ("/movies/scifi", true)),
        });
        Assert.Equal(2, plan.Count);
        Assert.Contains(new WatchPlanEntry("/movies", false), plan);
        Assert.Contains(new WatchPlanEntry("/movies/scifi", true), plan);
    }

    [Fact]
    public void Duplicate_paths_across_rules_dedupe()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies", true)),
            MakeRule(true, ("/movies", true)),
            MakeRule(true, ("/movies/", true)),
        });
        Assert.Single(plan);
    }

    [Fact]
    public void Disabled_rules_are_ignored()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies", true)),
            MakeRule(false, ("/tv", true)),
        });
        Assert.Single(plan);
        Assert.Equal("/movies", plan[0].Path);
    }

    [Fact]
    public void Independent_subtrees_are_kept()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies/scifi", true)),
            MakeRule(true, ("/movies/thriller", true)),
        });
        Assert.Equal(2, plan.Count);
    }

    [Fact]
    public void Trailing_slashes_normalize()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("/movies/", true)),
            MakeRule(true, ("/movies", true)),
        });
        Assert.Single(plan);
        Assert.Equal("/movies", plan[0].Path);
    }

    [Fact]
    public void Empty_or_whitespace_source_paths_excluded()
    {
        var plan = WatchPlanner.Plan(new[]
        {
            MakeRule(true, ("", true), ("   ", false), ("/movies", true)),
        });
        Assert.Single(plan);
        Assert.Equal("/movies", plan[0].Path);
    }

    [Fact]
    public void Many_rules_one_root_collapses_to_one_watch()
    {
        // 50 rules all rooted at /data with deep sub-paths should reduce to ONE watch.
        var rules = Enumerable.Range(0, 50)
            .Select(i => MakeRule(true, ($"/data/sub-{i:D3}", true)))
            .Append(MakeRule(true, ("/data", true)))
            .ToList();
        var plan = WatchPlanner.Plan(rules);
        Assert.Single(plan);
        Assert.Equal("/data", plan[0].Path);
    }
}
