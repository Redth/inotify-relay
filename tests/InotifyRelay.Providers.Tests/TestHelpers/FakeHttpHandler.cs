using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace InotifyRelay.Providers.Tests.TestHelpers;

/// <summary>Records every outgoing HttpRequestMessage; pluggable response function.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = new();
    public Func<HttpRequestMessage, int, HttpResponseMessage>? Responder { get; set; }

    public int RequestCount => Requests.Count;
    public RecordedRequest Last => Requests[^1];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req, CancellationToken cancellationToken)
    {
        var body = req.Content is null ? null : await req.Content.ReadAsStringAsync(cancellationToken);
        var headers = req.Headers
            .Concat(req.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

        var rec = new RecordedRequest(
            req.Method.Method,
            req.RequestUri!,
            headers,
            body,
            req.Content?.Headers.ContentType?.MediaType);
        Requests.Add(rec);

        var resp = Responder?.Invoke(req, Requests.Count) ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
        };
        return resp;
    }
}

public sealed record RecordedRequest(
    string Method,
    Uri Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? ContentType);

public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public FakeHttpHandler Handler { get; } = new();
    public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
}
