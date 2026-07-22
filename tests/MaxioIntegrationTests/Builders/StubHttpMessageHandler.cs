using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Stands in for the Maxio API. Routes are matched on method and path so a test can assert both
/// what the client sent and how it handled what came back.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>
    /// Every request the client issued, in order, with its body captured.
    /// </summary>
    public List<CapturedRequest> Requests { get; } = new();

    /// <summary>
    /// The full URI of every request, so tests can assert which server was actually targeted.
    /// </summary>
    public List<string> AbsoluteUris { get; } = new();

    /// <summary>
    /// Queues a JSON response for a method and path. The path is matched on the URI's absolute
    /// path plus query, so "handle:" segments and query strings are asserted too.
    /// </summary>
    public StubHttpMessageHandler RespondWith(HttpMethod method, string pathAndQuery, HttpStatusCode status,
        string json)
    {
        _routes.Add(new Route(method, pathAndQuery, status, json, null));
        return this;
    }

    /// <summary>
    /// Queues a transport-level failure, as if the provider could not be reached.
    /// </summary>
    public StubHttpMessageHandler FailWith(HttpMethod method, string pathAndQuery, Exception exception)
    {
        _routes.Add(new Route(method, pathAndQuery, HttpStatusCode.OK, null, exception));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var pathAndQuery = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery).TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedRequest(request.Method, pathAndQuery, body,
            request.Headers.Authorization?.ToString()));
        AbsoluteUris.Add(request.RequestUri.AbsoluteUri);

        var route = _routes.FirstOrDefault(candidate =>
            candidate.Method == request.Method &&
            string.Equals(candidate.PathAndQuery, pathAndQuery, StringComparison.Ordinal));

        if (route is null)
        {
            throw new InvalidOperationException(
                $"No stubbed response for {request.Method} {pathAndQuery}. " +
                $"Stubbed: {string.Join(", ", _routes.Select(r => $"{r.Method} {r.PathAndQuery}"))}");
        }

        if (route.Exception is not null)
        {
            throw route.Exception;
        }

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Json ?? string.Empty, Encoding.UTF8, "application/json")
        };
    }

    private record Route(HttpMethod Method, string PathAndQuery, HttpStatusCode Status, string? Json,
        Exception? Exception);

    public record CapturedRequest(HttpMethod Method, string PathAndQuery, string? Body, string? Authorization);
}
