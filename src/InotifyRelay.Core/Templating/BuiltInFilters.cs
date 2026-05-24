using System.Text.RegularExpressions;

namespace InotifyRelay.Core.Templating;

public sealed class TemplateFilterRegistry : ITemplateFilterRegistry
{
    private readonly Dictionary<string, TemplateFilter> _filters = new(StringComparer.OrdinalIgnoreCase);

    public TemplateFilterRegistry Register(string name, TemplateFilter filter)
    {
        _filters[name] = filter;
        return this;
    }

    public bool TryGet(string name, out TemplateFilter? filter)
    {
        var found = _filters.TryGetValue(name, out var f);
        filter = f;
        return found;
    }

    public IEnumerable<string> Names => _filters.Keys;

    public static TemplateFilterRegistry CreateDefault()
    {
        var r = new TemplateFilterRegistry();
        r.Register("lower", (v, _) => v.ToLowerInvariant());
        r.Register("upper", (v, _) => v.ToUpperInvariant());
        r.Register("trim",  (v, _) => v.Trim());
        r.Register("urlencode", (v, _) => Uri.EscapeDataString(v));
        r.Register("jsonescape", (v, _) => System.Text.Json.JsonEncodedText.Encode(v).ToString());

        r.Register("replace", (v, a) =>
        {
            Require(a, 2, "replace");
            return v.Replace(a[0], a[1]);
        });

        r.Register("regex", (v, a) =>
        {
            Require(a, 2, "regex");
            return Regex.Replace(v, a[0], a[1]);
        });

        r.Register("prefix", (v, a) => { Require(a, 1, "prefix"); return a[0] + v; });
        r.Register("suffix", (v, a) => { Require(a, 1, "suffix"); return v + a[0]; });

        r.Register("combine", (v, a) =>
        {
            Require(a, 1, "combine");
            return Path.Combine(a[0], v.TrimStart('/', '\\'));
        });

        r.Register("relativeTo", (v, a) =>
        {
            Require(a, 1, "relativeTo");
            var b = a[0].TrimEnd('/', '\\');
            return v.StartsWith(b, StringComparison.Ordinal)
                ? v[b.Length..].TrimStart('/', '\\')
                : v;
        });

        r.Register("default", (v, a) =>
        {
            Require(a, 1, "default");
            return string.IsNullOrEmpty(v) ? a[0] : v;
        });

        return r;
    }

    private static void Require(IReadOnlyList<string> a, int n, string name)
    {
        if (a.Count < n)
            throw new TemplateException($"Filter '{name}' requires {n} argument(s), got {a.Count}");
    }
}
