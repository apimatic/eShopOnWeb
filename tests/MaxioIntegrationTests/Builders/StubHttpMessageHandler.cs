using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Stands in for the Maxio server. Responses are queued per route so a test can script an
/// exchange, and every outbound request is captured so the test can assert on the exact
/// method, path and body the client produced.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<StubResponse> _responses = new();

    public List<CapturedRequest> Requests { get; } = new();

    /// <summary>Set to throw a transport failure instead of responding.</summary>
    public Exception? TransportFailure { get; set; }

    public CapturedRequest LastRequest => Requests[^1];

    /// <summary>Queues a response for the first request whose path contains <paramref name="pathContains"/>.</summary>
    public StubHttpMessageHandler RespondWith(string pathContains, HttpStatusCode statusCode, string json)
    {
        _responses.Add(new StubResponse(pathContains, statusCode, json));

        return this;
    }

    public StubHttpMessageHandler RespondWithOk(string pathContains, string json) =>
        RespondWith(pathContains, HttpStatusCode.OK, json);

    public StubHttpMessageHandler RespondWithNotFound(string pathContains) =>
        RespondWith(pathContains, HttpStatusCode.NotFound, "{\"errors\":[\"Not found\"]}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        if (TransportFailure is not null)
        {
            throw TransportFailure;
        }

        var path = request.RequestUri!.PathAndQuery;

        var match = _responses.FirstOrDefault(r => !r.Consumed && path.Contains(r.PathContains,
            StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new InvalidOperationException(
                $"No stubbed response for {request.Method} {path}. Stub one with RespondWith(...).");
        }

        match.Consumed = true;

        return new HttpResponseMessage(match.StatusCode)
        {
            Content = new StringContent(match.Json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubResponse
    {
        public StubResponse(string pathContains, HttpStatusCode statusCode, string json)
        {
            PathContains = pathContains;
            StatusCode = statusCode;
            Json = json;
        }

        public string PathContains { get; }
        public HttpStatusCode StatusCode { get; }
        public string Json { get; }
        public bool Consumed { get; set; }
    }
}

public record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? AuthScheme,
    string? AuthParameter);
