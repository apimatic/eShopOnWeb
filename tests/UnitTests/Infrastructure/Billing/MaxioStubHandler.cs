using System.Net;
using System.Net.Http;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// The <see cref="HttpClient"/> the SDK client is constructed from is the testing seam — no SDK internals are
/// mocked, and no network call happens.
/// </summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    public sealed record SentRequest(HttpMethod Method, string Path, string Query, string Body);

    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public MaxioStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    /// <summary>Every request that actually reached the wire, in order. Retries append, so this is the count.</summary>
    public List<SentRequest> Requests { get; } = new();

    public int CountOf(HttpMethod method, string path) =>
        Requests.Count(request => request.Method == method && request.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read the body here: HttpClient disposes request content once the call completes.
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new SentRequest(
            request.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.RequestUri?.Query ?? string.Empty,
            body));

        return _responder(request);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
