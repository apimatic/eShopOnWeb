using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

/// <summary>
/// The SDK takes an <see cref="HttpClient"/> in its constructor, so a fake message handler is the
/// seam these tests use — no SDK internals are mocked and no network call is made.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) => _responder = responder;

    /// <summary>Every request that reached the wire, in order. Retries append, so this is what you count.</summary>
    public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(request =>
            request.Method == method &&
            request.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase));

    public string? LastBodyFor(HttpMethod method, string pathFragment) =>
        Requests.LastOrDefault(request =>
            request.Method == method &&
            request.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase)).Body;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));

        return _responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
