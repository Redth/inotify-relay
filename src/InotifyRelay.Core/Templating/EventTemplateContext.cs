using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Templating;

public static class EventTemplateContext
{
    public static TemplateContext Build(FileSystemChange change, string ruleName, string ruleId)
    {
        var path = change.Path;
        var sourceRoot = change.SourceRoot.TrimEnd('/', '\\');
        var relativePath = path.StartsWith(sourceRoot, StringComparison.Ordinal)
            ? path[sourceRoot.Length..].TrimStart('/', '\\')
            : path;
        var filename = Path.GetFileName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var relativeDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;

        return new TemplateContext()
            .Set("path", path)
            .Set("relativePath", relativePath)
            .Set("sourceRoot", change.SourceRoot)
            .Set("filename", filename)
            .Set("name", name)
            .Set("ext", ext)
            .Set("directory", directory)
            .Set("relativeDirectory", relativeDirectory)
            .Set("event", change.EventType.ToString())
            .Set("isDirectory", change.IsDirectory ? "true" : "false")
            .Set("timestamp", change.Timestamp.ToString("o"))
            .Set("oldPath", change.OldPath)
            .Set("rule.name", ruleName)
            .Set("rule.id", ruleId);
    }
}
