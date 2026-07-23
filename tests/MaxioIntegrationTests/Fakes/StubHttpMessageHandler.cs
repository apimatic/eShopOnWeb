using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Stands in for Maxio at the HTTP boundary so the real <c>MaxioBillingClient</c> — its request
/// construction, JSON handling, unit conversion and error mapping — is exercised end to end without
/// leaving the test process. Every request is recorded so tests can assert what was actually sent.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    private StubHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest => Requests[^1];

    /// <summary>Answers every request with the same status and body.</summary>
    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string json)
    {
        return new StubHttpMessageHandler(_ => Respond(statusCode, json));
    }

    /// <summary>Answers 200 with <paramref name="json"/>.</summary>
    public static StubHttpMessageHandler ReturningOk(string json)
    {
        return Returning(HttpStatusCode.OK, json);
    }

    /// <summary>
    /// Answers by matching the request path against a route table, so a client method that makes
    /// several calls (lookup then create, record then read back) can be driven realistically.
    /// A path that is not in the table answers 404.
    /// </summary>
    public static StubHttpMessageHandler Routing(params (string PathContains, HttpStatusCode StatusCode, string Json)[] routes)
    {
        return new StubHttpMessageHandler(request =>
        {
            foreach (var route in routes)
            {
                if (request.Path.Contains(route.PathContains, StringComparison.OrdinalIgnoreCase))
                {
                    return Respond(route.StatusCode, route.Json);
                }
            }

            return Respond(HttpStatusCode.NotFound, "{\"errors\":[\"Not found\"]}");
        });
    }

    /// <summary>Answers each call in turn, so a repeated call can be given a different answer.</summary>
    public static StubHttpMessageHandler InSequence(params (HttpStatusCode StatusCode, string Json)[] responses)
    {
        var index = 0;
        return new StubHttpMessageHandler(_ =>
        {
            var response = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return Respond(response.StatusCode, response.Json);
        });
    }

    /// <summary>Fails the transport itself, standing in for an unreachable provider.</summary>
    public static StubHttpMessageHandler Unreachable(string message = "No such host is known.")
    {
        return new StubHttpMessageHandler(_ => throw new HttpRequestException(message));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The body has to be captured now: the client disposes the request once the call returns.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter);

        Requests.Add(recorded);

        return _responder(recorded);
    }

    private static HttpResponseMessage Respond(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri RequestUri, string? Body, string? AuthScheme, string? AuthParameter)
{
    public string Path => RequestUri.AbsolutePath;

    public string PathAndQuery => RequestUri.PathAndQuery;
}
