using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A stand-in for Maxio Advanced Billing. The SDK is constructed over an <see cref="HttpClient"/> we
/// supply, so this handler is the seam through which the integration's real wire behaviour — the URL
/// it calls, the body it sends, the credentials it presents and how it reacts to what comes back —
/// can be observed and driven.
/// </summary>
public sealed class FakeBillingProvider : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    /// <summary>Every request the integration made, in order.</summary>
    public List<RecordedRequest> Requests { get; } = new();

    public FakeBillingProvider Respond(HttpMethod method, string pathContains, string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _routes.Add(new Route(method, pathContains, _ => new StubResponse(statusCode, body, TimeSpan.Zero)));
        return this;
    }

    /// <summary>Answers differently on each successive call, so retry behaviour can be observed.</summary>
    public FakeBillingProvider RespondInSequence(HttpMethod method, string pathContains,
        params StubResponse[] responses)
    {
        var index = 0;
        _routes.Add(new Route(method, pathContains, _ =>
        {
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return response;
        }));

        return this;
    }

    public FakeBillingProvider RespondSlowly(HttpMethod method, string pathContains, TimeSpan delay)
    {
        _routes.Add(new Route(method, pathContains, _ => new StubResponse(HttpStatusCode.OK, "{}", delay)));
        return this;
    }

    /// <summary>How many requests hit a path, used to prove writes are never replayed.</summary>
    public int CallsTo(string pathContains) =>
        Requests.Count(request => request.Uri.PathAndQuery.Contains(pathContains, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body,
            request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));

        var route = _routes.FirstOrDefault(candidate => candidate.Matches(request));
        var stub = route?.Respond(request)
            ?? new StubResponse(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""", TimeSpan.Zero);

        if (stub.Delay > TimeSpan.Zero)
        {
            await Task.Delay(stub.Delay, cancellationToken);
        }

        return new HttpResponseMessage(stub.StatusCode)
        {
            Content = new StringContent(stub.Body, Encoding.UTF8, "application/json"),

            // The SDK's retry policy reads the method off the response's originating request, so a
            // handler that omits this silently disables retries.
            RequestMessage = request
        };
    }

    private sealed record Route(HttpMethod Method, string PathContains, Func<HttpRequestMessage, StubResponse> Respond)
    {
        public bool Matches(HttpRequestMessage request) =>
            request.Method == Method
            && request.RequestUri!.PathAndQuery.Contains(PathContains, StringComparison.Ordinal);
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? AuthScheme,
    string? AuthParameter);

public sealed record StubResponse(HttpStatusCode StatusCode, string Body, TimeSpan Delay)
{
    public static StubResponse Ok(string body) => new(HttpStatusCode.OK, body, TimeSpan.Zero);

    public static StubResponse Status(HttpStatusCode statusCode, string body = "{}") =>
        new(statusCode, body, TimeSpan.Zero);
}
