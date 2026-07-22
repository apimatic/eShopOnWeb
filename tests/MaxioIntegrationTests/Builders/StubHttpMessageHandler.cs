using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Stands in for the Maxio API. Records every request it receives - method, path, body and
/// Authorization header - and answers with the responses queued for the test.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler RespondWith(HttpStatusCode statusCode, string? json = null)
    {
        _responses.Enqueue(_ => Build(statusCode, json));
        return this;
    }

    public StubHttpMessageHandler RespondWith(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    /// <summary>Answers any request the queue does not cover - useful for follow-up reads.</summary>
    public StubHttpMessageHandler AlwaysRespondWith(HttpStatusCode statusCode, string? json = null)
    {
        _fallback = _ => Build(statusCode, json);
        return this;
    }

    public int CountOf(HttpMethod method, string pathFragment)
        => Requests.Count(r => r.Method == method && r.Path.Contains(pathFragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter));

        var responder = _responses.Count > 0
            ? _responses.Dequeue()
            : _fallback ?? throw new InvalidOperationException(
                $"No response queued for {request.Method} {request.RequestUri.AbsolutePath}.");

        return responder(request);
    }

    private static HttpResponseMessage Build(HttpStatusCode statusCode, string? json) => new(statusCode)
    {
        Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
    };
}

public record RecordedRequest(
    HttpMethod Method,
    string Path,
    string Query,
    string? Body,
    string? AuthScheme,
    string? AuthParameter);
