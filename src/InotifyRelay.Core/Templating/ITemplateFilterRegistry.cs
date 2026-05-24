namespace InotifyRelay.Core.Templating;

public delegate string TemplateFilter(string input, IReadOnlyList<string> args);

public interface ITemplateFilterRegistry
{
    bool TryGet(string name, out TemplateFilter? filter);
    IEnumerable<string> Names { get; }
}
