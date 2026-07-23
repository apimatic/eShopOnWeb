using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// A scripted HTTP stub standing in for the Maxio host.
/// </summary>
/// <remarks>
/// The SDK client is constructed from an <see cref="HttpClient"/>, which makes the message handler the
/// natural test seam. Responses are dequeued in order, and every outgoing request is captured so a test
/// can assert what the integration actually sent — route, verb and body — not merely that it returned.
/// An unscripted request fails loudly instead of silently succeeding.
/// </remarks>
internal sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly Queue<Func<CapturedRequest, HttpResponseMessage>> _responses = new();

    public List<CapturedRequest> Requests { get; } = new();

    public CapturedRequest LastRequest => Requests.Count > 0
        ? Requests[^1]
        : throw new InvalidOperationException("No request was sent.");

    /// <summary>Scripts the next response as JSON with the given status.</summary>
    public FakeMaxioHandler Enqueue(HttpStatusCode status, string json)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    /// <summary>Scripts the next response as a 200 with the given JSON body.</summary>
    public FakeMaxioHandler EnqueueOk(string json) => Enqueue(HttpStatusCode.OK, json);

    /// <summary>
    /// Makes every request fail to connect, the way an unreachable host does. It is sticky rather than
    /// one-shot because the SDK retries idempotent verbs, so a single scripted failure would not model
    /// a host that is actually down.
    /// </summary>
    public FakeMaxioHandler AlwaysFailToConnect(string message = "connection refused")
    {
        _connectionFailure = message;
        return this;
    }

    private string? _connectionFailure;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Parameter,
            request.Headers.Authorization?.Scheme);

        Requests.Add(captured);

        if (_connectionFailure is not null)
        {
            throw new HttpRequestException(_connectionFailure);
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"The integration sent an unscripted request: {captured.Method} {captured.Uri}. " +
                "Script a response for it, or the test is asserting against a call it did not expect.");
        }

        return _responses.Dequeue()(captured);
    }
}

/// <summary>One outgoing request, captured for assertion.</summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    string? AuthorizationParameter,
    string? AuthorizationScheme)
{
    public string Path => Uri.AbsolutePath;

    public string Query => Uri.Query;

    /// <summary>The username half of the HTTP Basic header — for Maxio, the site API key.</summary>
    public string? BasicAuthUserName
    {
        get
        {
            if (AuthorizationParameter is null)
            {
                return null;
            }

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(AuthorizationParameter));
            return decoded.Split(':', 2)[0];
        }
    }
}
