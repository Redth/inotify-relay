using System.Text.Json;

namespace InotifyRelay.Web.Services;

/// <summary>JSON pretty-printer. Invalid input is returned unchanged so we never lose user data.</summary>
public static class JsonFormat
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Pretty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json ?? "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Indented);
        }
        catch
        {
            return json;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Indented);
}
