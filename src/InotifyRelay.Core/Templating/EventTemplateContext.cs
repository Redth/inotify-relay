using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Templating;

public static class EventTemplateContext
{
    /// <summary>
    /// Build a template context for an event going to a specific target.
    /// When <paramref name="pathMappings"/> is non-empty, the path-derived
    /// variables (<c>path</c>, <c>directory</c>, <c>sourceRoot</c>) are rewritten
    /// to the target's view. The raw originals stay accessible as
    /// <c>source.path</c>, <c>source.directory</c>, <c>source.sourceRoot</c>.
    /// </summary>
    public static TemplateContext Build(
        FileSystemChange change,
        string ruleName,
        string ruleId,
        IReadOnlyList<PathMapping>? pathMappings = null)
    {
        var rawPath = change.Path;
        var rawSourceRoot = change.SourceRoot;
        var rawDirectory = Path.GetDirectoryName(rawPath) ?? string.Empty;

        var path = PathMapper.Apply(rawPath, pathMappings);
        var sourceRoot = PathMapper.Apply(rawSourceRoot, pathMappings);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;

        // relativePath stays invariant — it's computed from the raw pair to remain
        // stable regardless of whether the source or target side gets remapped.
        var srcTrim = rawSourceRoot.TrimEnd('/', '\\');
        var relativePath = rawPath.StartsWith(srcTrim, StringComparison.Ordinal)
            ? rawPath[srcTrim.Length..].TrimStart('/', '\\')
            : rawPath;
        var relativeDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;

        var filename = Path.GetFileName(rawPath);
        var name = Path.GetFileNameWithoutExtension(rawPath);
        var ext = Path.GetExtension(rawPath);

        return new TemplateContext()
            .Set("path", path)
            .Set("directory", directory)
            .Set("sourceRoot", sourceRoot)
            .Set("source.path", rawPath)
            .Set("source.directory", rawDirectory)
            .Set("source.sourceRoot", rawSourceRoot)
            .Set("relativePath", relativePath)
            .Set("relativeDirectory", relativeDirectory)
            .Set("filename", filename)
            .Set("name", name)
            .Set("ext", ext)
            .Set("event", change.EventType.ToString())
            .Set("isDirectory", change.IsDirectory ? "true" : "false")
            .Set("timestamp", change.Timestamp.ToString("o"))
            .Set("oldPath", change.OldPath is null ? null : PathMapper.Apply(change.OldPath, pathMappings))
            .Set("source.oldPath", change.OldPath)
            .Set("rule.name", ruleName)
            .Set("rule.id", ruleId);
    }
}
