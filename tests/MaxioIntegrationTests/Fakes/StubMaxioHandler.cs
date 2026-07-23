using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// The test seam for the Maxio SDK: an <see cref="HttpMessageHandler"/> that answers routed requests
/// with canned Maxio payloads and records everything that went out.
/// </summary>
/// <remarks>
/// Requests that match no route fail the test loudly rather than returning a default, so a client
/// change that alters the verb or path cannot pass silently.
/// </remarks>
internal sealed class StubMaxioHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    public List<CapturedRequest> Requests { get; } = new();

    public StubMaxioHandler Map(HttpMethod method, string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add(new Route(method, pathContains, _ => (status, json)));

        return this;
    }

    public StubMaxioHandler Map(HttpMethod method, string pathContains, Func<CapturedRequest, (HttpStatusCode Status, string Json)> responder)
    {
        _routes.Add(new Route(method, pathContains, responder));

        return this;
    }

    public IReadOnlyList<CapturedRequest> RequestsFor(HttpMethod method, string pathContains) =>
        Requests.Where(r => r.Method == method && r.Path.Contains(pathContains, StringComparison.Ordinal)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var uri = request.RequestUri!;
        var captured = new CapturedRequest(request.Method, uri.AbsolutePath, uri.Query, body, request.Headers.Authorization?.ToString());

        Requests.Add(captured);

        // Later registrations win, so a test can override a default route set up by a fixture.
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            var route = _routes[i];
            if (route.Method != request.Method || !captured.Path.Contains(route.PathContains, StringComparison.Ordinal))
            {
                continue;
            }

            var (status, json) = route.Responder(captured);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }

        throw new InvalidOperationException(
            $"No stub route matched {request.Method} {captured.Path}. Add a Map(...) for it, or fix the client's request.");
    }

    private sealed record Route(HttpMethod Method, string PathContains, Func<CapturedRequest, (HttpStatusCode, string)> Responder);
}

internal sealed record CapturedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization);
