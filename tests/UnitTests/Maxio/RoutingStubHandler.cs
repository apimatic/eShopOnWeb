using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// A test double for the SDK's transport seam: the <see cref="HttpClient"/> passed to
/// <c>MaxioClient</c>. Routes each request through a caller-supplied responder and records the method, path
/// and (buffered) body of every request so tests can assert what the SDK actually sent. The body is buffered
/// inside <see cref="SendAsync"/> because the SDK disposes request content per attempt.
/// </summary>
public sealed class RoutingStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<(HttpMethod Method, string Path, string? Body)> Calls { get; } = new();

    public RoutingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));

        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public int CountCalls(HttpMethod method, string pathSuffix) =>
        Calls.Count(c => c.Method == method && c.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase));

    public string? BodyOf(HttpMethod method, string pathSuffix) =>
        Calls.FirstOrDefault(c => c.Method == method && c.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase)).Body;

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);
}
