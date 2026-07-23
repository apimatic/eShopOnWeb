using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// A fake transport for the Maxio SDK. The <see cref="HttpClient"/> the billing client is
/// constructed with is the only seam the SDK exposes, so responses are queued here in the order
/// the client is expected to make them and every outgoing request is captured for assertion.
/// </summary>
/// <remarks>
/// An unexpected extra request throws rather than returning a default response, so a test fails
/// loudly if the client starts making calls it should not.
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<QueuedResponse> _responses = new();
    private readonly List<CapturedRequest> _requests = new();

    /// <summary>Every request the client issued, in order.</summary>
    public IReadOnlyList<CapturedRequest> Requests => _requests;

    /// <summary>Queues a successful JSON response.</summary>
    public StubHttpMessageHandler RespondWithJson(string json) =>
        Respond(HttpStatusCode.OK, json);

    /// <summary>Queues a response with an explicit status code.</summary>
    public StubHttpMessageHandler Respond(HttpStatusCode statusCode, string json)
    {
        _responses.Enqueue(new QueuedResponse(statusCode, json));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.Parameter));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"The billing client made an unexpected request: {request.Method} {request.RequestUri}. " +
                $"{_requests.Count} request(s) were made but no further response was queued.");
        }

        var queued = _responses.Dequeue();

        return new HttpResponseMessage(queued.StatusCode)
        {
            Content = new StringContent(queued.Json, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    private sealed record QueuedResponse(HttpStatusCode StatusCode, string Json);
}

/// <summary>One outgoing request, captured for assertion.</summary>
/// <param name="Method">The HTTP verb used.</param>
/// <param name="Uri">The fully composed request URI.</param>
/// <param name="Body">The serialized request body, if the request had one.</param>
/// <param name="AuthorizationParameter">The Basic-auth parameter, if credentials were attached.</param>
public sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body, string? AuthorizationParameter);
