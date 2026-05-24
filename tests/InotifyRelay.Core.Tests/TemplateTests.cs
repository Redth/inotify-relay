using InotifyRelay.Core.Templating;

namespace InotifyRelay.Core.Tests;

public class TemplateTests
{
    static readonly TemplateFilterRegistry Filters = TemplateFilterRegistry.CreateDefault();

    [Fact]
    public void Literal_only_passes_through()
    {
        var t = Template.Parse("hello world");
        Assert.Equal("hello world", t.Render(new TemplateContext(), Filters));
    }

    [Fact]
    public void Substitutes_variables()
    {
        var t = Template.Parse("path={path}");
        var ctx = new TemplateContext().Set("path", "/a/b.mkv");
        Assert.Equal("path=/a/b.mkv", t.Render(ctx, Filters));
    }

    [Fact]
    public void Applies_single_filter()
    {
        var t = Template.Parse("{name|upper}");
        Assert.Equal("HELLO", t.Render(new TemplateContext().Set("name", "hello"), Filters));
    }

    [Fact]
    public void Replace_filter_with_args()
    {
        var t = Template.Parse("{path|replace:'/watch':'/media'}");
        Assert.Equal("/media/movies/x.mkv", t.Render(new TemplateContext().Set("path", "/watch/movies/x.mkv"), Filters));
    }

    [Fact]
    public void Chained_filters_apply_left_to_right()
    {
        var t = Template.Parse("{p|replace:'a':'b'|upper}");
        Assert.Equal("BBC", t.Render(new TemplateContext().Set("p", "abc"), Filters));
    }

    [Fact]
    public void Default_filter_provides_fallback_when_empty()
    {
        var t = Template.Parse("{missing|default:'n/a'}");
        Assert.Equal("n/a", t.Render(new TemplateContext(), Filters));
    }

    [Fact]
    public void Escaped_braces_pass_through()
    {
        var t = Template.Parse("{{not a var}} {x}");
        Assert.Equal("{not a var} 1", t.Render(new TemplateContext().Set("x", "1"), Filters));
    }

    [Fact]
    public void Unterminated_placeholder_throws()
    {
        Assert.Throws<TemplateException>(() => Template.Parse("{path"));
    }

    [Fact]
    public void Json_literal_braces_are_not_placeholders()
    {
        // The default webhook body template is JSON. The outer { and } must be
        // literal, while {event} and {path} interpolate.
        var t = Template.Parse(@"{""event"":""{event}"",""path"":""{path}""}");
        var ctx = new TemplateContext().Set("event", "ClosedWrite").Set("path", "/x/a.mkv");
        Assert.Equal(@"{""event"":""ClosedWrite"",""path"":""/x/a.mkv""}", t.Render(ctx, Filters));
    }

    [Fact]
    public void Lone_closing_brace_is_literal()
    {
        var t = Template.Parse("a } b");
        Assert.Equal("a } b", t.Render(new TemplateContext(), Filters));
    }

    [Fact]
    public void Unknown_filter_throws_at_render()
    {
        var t = Template.Parse("{x|nope}");
        Assert.Throws<TemplateException>(() => t.Render(new TemplateContext().Set("x", "v"), Filters));
    }
}
