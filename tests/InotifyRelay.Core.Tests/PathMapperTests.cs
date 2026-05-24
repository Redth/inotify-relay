using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class PathMapperTests
{
    [Fact]
    public void Empty_or_null_mappings_pass_through()
    {
        Assert.Equal("/x", PathMapper.Apply("/x", null));
        Assert.Equal("/x", PathMapper.Apply("/x", Array.Empty<PathMapping>()));
    }

    [Fact]
    public void Rewrites_prefix_to_target()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        Assert.Equal("/data/movies/Inception/a.mkv",
            PathMapper.Apply("/watch/movies/Inception/a.mkv", maps));
    }

    [Fact]
    public void Exact_match_returns_to()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        Assert.Equal("/data/movies", PathMapper.Apply("/watch/movies", maps));
    }

    [Fact]
    public void Longest_prefix_wins()
    {
        var maps = new[]
        {
            new PathMapping("/watch", "/A"),
            new PathMapping("/watch/movies", "/data/movies"),
        };
        Assert.Equal("/data/movies/x.mkv", PathMapper.Apply("/watch/movies/x.mkv", maps));
        Assert.Equal("/A/tv/y.mkv", PathMapper.Apply("/watch/tv/y.mkv", maps));
    }

    [Fact]
    public void Segment_boundary_prevents_false_match()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        // "/watch/moviestars" must NOT be rewritten by "/watch/movies"
        Assert.Equal("/watch/moviestars/x.mkv",
            PathMapper.Apply("/watch/moviestars/x.mkv", maps));
    }

    [Fact]
    public void Trailing_slashes_on_either_side_normalize()
    {
        var maps = new[] { new PathMapping("/watch/movies/", "/data/movies/") };
        Assert.Equal("/data/movies/x.mkv",
            PathMapper.Apply("/watch/movies/x.mkv", maps));
    }

    [Fact]
    public void No_match_returns_input_unchanged()
    {
        var maps = new[] { new PathMapping("/watch/movies", "/data/movies") };
        Assert.Equal("/other/path", PathMapper.Apply("/other/path", maps));
    }

    [Fact]
    public void Empty_from_is_ignored()
    {
        var maps = new[] { new PathMapping("", "/everything"), new PathMapping("/x", "/y") };
        Assert.Equal("/y/a", PathMapper.Apply("/x/a", maps));
        Assert.Equal("/other", PathMapper.Apply("/other", maps));
    }

    [Fact]
    public void Empty_input_passes_through()
    {
        var maps = new[] { new PathMapping("/x", "/y") };
        Assert.Equal("", PathMapper.Apply("", maps));
    }
}
