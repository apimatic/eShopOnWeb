using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The test seam: an <see cref="HttpMessageHandler"/> that answers the billing client with canned
/// responses and records every outbound request, so tests can assert both what was sent and how the
/// response was interpreted. Nothing here reaches a real Maxio site.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    public List<RecordedRequest> Requests { get; } = new();

    public RecordedRequest LastRequest => Requests[^1];

    /// <summary>Queues a successful JSON response.</summary>
    public StubHttpMessageHandler RespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        return this;
    }

    /// <summary>Queues a failure response carrying the given body.</summary>
    public StubHttpMessageHandler RespondWithError(HttpStatusCode status, string body = "")
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        return this;
    }

    /// <summary>
    /// Makes every request fail at the connection level, as though the provider were unreachable.
    /// This is sticky rather than queued because the SDK retries idempotent requests.
    /// </summary>
    public StubHttpMessageHandler AlwaysFailTransport()
    {
        _fallback = _ => throw new HttpRequestException("No such host is known.");

        return this;
    }

    /// <summary>
    /// Makes every request fail with the given status. Sticky, so a retried request keeps failing.
    /// </summary>
    public StubHttpMessageHandler AlwaysRespondWithError(HttpStatusCode status, string body = "")
    {
        _fallback = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body,
            request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));

        if (_responses.Count > 0)
        {
            return _responses.Dequeue()(request);
        }

        if (_fallback is not null)
        {
            return _fallback(request);
        }

        throw new InvalidOperationException(
            $"The billing client made an unexpected {request.Method} request to {request.RequestUri}.");
    }

    public record RecordedRequest(HttpMethod Method,
        Uri Uri,
        string? Body,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
