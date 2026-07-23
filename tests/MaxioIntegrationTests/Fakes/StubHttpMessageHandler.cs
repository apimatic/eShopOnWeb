using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Stands in for Maxio. Responses are queued in the order the client is expected to make calls,
/// and every request is captured so a test can assert the exact verb, path and body that went out.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<CapturedRequest> Requests { get; } = new();

    /// <summary>Number of requests the client actually made.</summary>
    public int RequestCount => Requests.Count;

    public StubHttpMessageHandler RespondWith(HttpStatusCode statusCode, string json)
    {
        _responders.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    public StubHttpMessageHandler RespondWithJson(string json) => RespondWith(HttpStatusCode.OK, json);

    /// <summary>Responds with a body that is not JSON at all — how Maxio answers a bad API key.</summary>
    public StubHttpMessageHandler RespondWithText(HttpStatusCode statusCode, string text)
    {
        _responders.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        });

        return this;
    }

    /// <summary>Simulates the provider being unreachable.</summary>
    public StubHttpMessageHandler RespondWithTransportFailure()
    {
        _responders.Enqueue(_ => throw new HttpRequestException("The remote name could not be resolved."));

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new CapturedRequest(request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException(
                $"The client made an unexpected request: {request.Method} {request.RequestUri}");
        }

        return _responders.Dequeue()(request);
    }
}

public record CapturedRequest(HttpMethod Method,
    Uri Uri,
    string? Body,
    string? AuthenticationScheme,
    string? AuthenticationParameter)
{
    public string Path => Uri.AbsolutePath;

    public string PathAndQuery => Uri.PathAndQuery;
}
