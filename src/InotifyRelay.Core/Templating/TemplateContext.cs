namespace InotifyRelay.Core.Templating;

public sealed class TemplateContext
{
    private readonly Dictionary<string, string?> _values;

    public TemplateContext(IEnumerable<KeyValuePair<string, string?>>? values = null)
    {
        _values = values is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public TemplateContext Set(string key, string? value)
    {
        _values[key] = value;
        return this;
    }

    public string? Get(string key)
    {
        if (_values.TryGetValue(key, out var v)) return v;

        if (key.StartsWith("env.", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(key[4..]);

        return null;
    }
}
