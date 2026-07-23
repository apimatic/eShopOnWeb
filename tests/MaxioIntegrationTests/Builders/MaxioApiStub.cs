using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// A stand-in for the Maxio HTTP API. Responses are registered per verb and path exactly as the
/// OpenAPI specification defines them, and every request is recorded so tests can assert what the
/// client actually put on the wire. An unregistered request throws rather than answering, so a
/// client that starts calling the wrong route fails loudly instead of silently.
/// </summary>
public class MaxioApiStub : HttpMessageHandler
{
    private readonly List<StubbedResponse> _responses = new List<StubbedResponse>();

    public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

    public RecordedRequest LastRequest => Requests[^1];

    public MaxioApiStub Respond(HttpMethod method, string pathAndQuery, string json,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Add(new StubbedResponse(method, pathAndQuery, status, json, null));
        return this;
    }

    public MaxioApiStub RespondWithFailure(HttpMethod method, string pathAndQuery, HttpStatusCode status, string json)
    {
        _responses.Add(new StubbedResponse(method, pathAndQuery, status, json, null));
        return this;
    }

    /// <summary>Registers a route that fails at the transport level, as an unreachable provider would.</summary>
    public MaxioApiStub RespondWithTransportFailure(HttpMethod method, string pathAndQuery, Exception exception)
    {
        _responses.Add(new StubbedResponse(method, pathAndQuery, HttpStatusCode.OK, string.Empty, exception));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, pathAndQuery, body,
            request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter,
            request.RequestUri.GetLeftPart(UriPartial.Authority)));

        var match = _responses.FirstOrDefault(response =>
            response.Method == request.Method && response.PathAndQuery == pathAndQuery);

        if (match is null)
        {
            throw new InvalidOperationException(
                $"The client sent an unexpected request: {request.Method} {pathAndQuery}");
        }

        if (match.TransportFailure is not null)
        {
            throw match.TransportFailure;
        }

        return new HttpResponseMessage(match.Status)
        {
            Content = new StringContent(match.Json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private record StubbedResponse(HttpMethod Method, string PathAndQuery, HttpStatusCode Status, string Json,
        Exception? TransportFailure);
}

public record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body, string? AuthorizationScheme,
    string? AuthorizationParameter, string Authority);
