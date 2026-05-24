namespace InotifyRelay.Core.Templating;

public sealed class Template
{
    public string Source { get; }
    internal IReadOnlyList<TemplateToken> Tokens { get; }

    internal Template(string source, IReadOnlyList<TemplateToken> tokens)
    {
        Source = source;
        Tokens = tokens;
    }

    public static Template Parse(string source) => TemplateParser.Parse(source);

    public string Render(TemplateContext context, ITemplateFilterRegistry filters)
    {
        var sb = new System.Text.StringBuilder(Source.Length);
        foreach (var token in Tokens)
        {
            token.Render(sb, context, filters);
        }
        return sb.ToString();
    }
}
