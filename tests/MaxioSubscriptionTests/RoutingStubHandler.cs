using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.MaxioSubscriptionTests;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that routes each request to a canned response by
/// (method, path). It records every request so tests can assert what the SDK actually sent (e.g.
/// that a subscription create was NOT issued when one already existed).
/// </summary>
public sealed class RoutingStubHandler : HttpMessageHandler
{
    public sealed record Stub(HttpStatusCode Status, string Json);

    private readonly Func<HttpRequestMessage, Stub> _router;

    public List<(HttpMethod Method, string Path, string? Query)> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public RoutingStubHandler(Func<HttpRequestMessage, Stub> router) => _router = router;

    public int Count(HttpMethod method, string pathContains) =>
        Requests.Count(r => r.Method == method && r.Path.Contains(pathContains));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method, path, request.RequestUri.Query));
        Bodies.Add(request.Content?.ReadAsStringAsync(ct).Result);

        var stub = _router(request);
        var response = new HttpResponseMessage(stub.Status)
        {
            Content = new StringContent(stub.Json, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
        return Task.FromResult(response);
    }
}
