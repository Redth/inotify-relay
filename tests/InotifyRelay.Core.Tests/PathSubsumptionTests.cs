using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class PathSubsumptionTests
{
    [Theory]
    [InlineData("/a", "/a/b", true)]
    [InlineData("/a", "/a/b/c", true)]
    [InlineData("/a/", "/a/b", true)]              // trailing slash on ancestor
    [InlineData("/a", "/a", true)]                 // equal counts as ancestor
    [InlineData("/a", "/ab", false)]               // prefix-not-segment-boundary
    [InlineData("/a", "/b/a", false)]
    [InlineData("",  "/anything", true)]          // empty is universal ancestor
    public void IsAncestorOrEqual_correctness(string ancestor, string descendant, bool expected)
        => Assert.Equal(expected, PathSubsumption.IsAncestorOrEqual(ancestor, descendant));

    [Fact]
    public void Drops_descendants_when_ancestor_is_in_the_set()
    {
        var input = new[]
        {
            "/movies/Inception/a.mkv",
            "/movies/Inception",
            "/movies/Inception/b.mkv",
            "/movies/Memento/x.mkv",
        };
        var result = PathSubsumption.Subsume(input, x => x);
        Assert.Equal(new[] { "/movies/Inception", "/movies/Memento/x.mkv" }.OrderBy(s => s),
                     result.OrderBy(s => s));
    }

    [Fact]
    public void Keeps_independent_subtrees_when_no_common_ancestor_queued()
    {
        var input = new[]
        {
            "/movies/Inception/a.mkv",
            "/tv/Severance/s1e1.mkv",
        };
        var result = PathSubsumption.Subsume(input, x => x);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Full_scan_marker_subsumes_everything()
    {
        var input = new[] { "/movies/a.mkv", "", "/tv/b.mkv" };
        var result = PathSubsumption.Subsume(input, x => x);
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void Duplicate_paths_collapse_to_one()
    {
        var input = new[] { "/x/a", "/x/a", "/x/a" };
        Assert.Single(PathSubsumption.Subsume(input, x => x));
    }

    [Fact]
    public void Trailing_slashes_normalize()
    {
        var input = new[] { "/x/", "/x/a" };
        Assert.Single(PathSubsumption.Subsume(input, x => x));
    }

    [Fact]
    public void Works_with_arbitrary_item_type_via_selector()
    {
        var input = new[]
        {
            (id: 1, path: "/a/b"),
            (id: 2, path: "/a"),
            (id: 3, path: "/c"),
        };
        var result = PathSubsumption.Subsume(input, x => x.path);
        Assert.Equal(new[] { 2, 3 }, result.Select(r => r.id).OrderBy(i => i));
    }
}
