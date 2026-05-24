using System.Text;

namespace InotifyRelay.Core.Templating;

internal abstract class TemplateToken
{
    public abstract void Render(StringBuilder sb, TemplateContext context, ITemplateFilterRegistry filters);
}

internal sealed class LiteralToken(string text) : TemplateToken
{
    public override void Render(StringBuilder sb, TemplateContext context, ITemplateFilterRegistry filters)
        => sb.Append(text);
}

internal sealed record FilterCall(string Name, IReadOnlyList<string> Args);

internal sealed class VariableToken(string name, IReadOnlyList<FilterCall> filters) : TemplateToken
{
    public override void Render(StringBuilder sb, TemplateContext context, ITemplateFilterRegistry filterRegistry)
    {
        var value = context.Get(name) ?? string.Empty;
        foreach (var f in filters)
        {
            if (!filterRegistry.TryGet(f.Name, out var fn))
                throw new TemplateException($"Unknown filter '{f.Name}'");
            value = fn!(value, f.Args);
        }
        sb.Append(value);
    }
}

public sealed class TemplateException(string message) : Exception(message);
