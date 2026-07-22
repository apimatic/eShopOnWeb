using System.Net;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// A test double for the outbound HTTP boundary. It records every request the
/// <c>MaxioBillingClient</c> makes and returns responses supplied by the test — either a single
/// scripted response or a routing function keyed on method + path. This lets tests assert the
/// integration's REAL behaviour (correct verbs, paths, bodies, and how it maps/normalises the
/// provider's JSON) without any live Maxio call.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, (HttpStatusCode status, string body)> _responder;

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, string, (HttpStatusCode status, string body)> responder)
    {
        _responder = responder;
    }

    /// <summary>Always answers with the given status/body regardless of the request.</summary>
    public static StubHttpMessageHandler Always(HttpStatusCode status, string body)
        => new((_, _) => (status, body));

    /// <summary>Throws a transport exception to simulate an unreachable provider.</summary>
    public static StubHttpMessageHandler NetworkFailure()
        => new((_, _) => throw new HttpRequestException("Simulated connection failure"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body,
            request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));

        var (status, responseBody) = _responder(request, body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? AuthScheme, string? AuthParameter)
{
    public string PathAndQuery => Uri.PathAndQuery;
}
