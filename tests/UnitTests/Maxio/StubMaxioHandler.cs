using System.Net;
using System.Text;
using Microsoft.eShopWeb.UnitTests.Maxio;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Stub HTTP transport standing in for Maxio Advanced Billing: routes by URL fragment and
/// records every request so tests can assert on outgoing method/path/body and on send counts.
/// </summary>
public sealed class StubMaxioHandler : HttpMessageHandler
{
    private sealed record Route(
        HttpMethod Method,
        string PathContains,
        Func<HttpRequestMessage, string> Body,
        HttpStatusCode Status = HttpStatusCode.OK,
        Func<HttpRequestMessage, (HttpStatusCode, string)>? Dynamic = null);

    private readonly List<Route> _routes = new();
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();

    public void OnGet(string pathContains, string jsonBody, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes.Add(new Route(HttpMethod.Get, pathContains, _ => jsonBody, status));

    public void OnGet(string pathContains, Func<HttpRequestMessage, string> body, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes.Add(new Route(HttpMethod.Get, pathContains, body, status));

    /// <summary>Registers a route whose status AND body are decided per call (e.g. queued responses).</summary>
    public void OnRespond(HttpMethod method, string pathContains, Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder) =>
        _routes.Add(new Route(method, pathContains, r => responder(r).Body, default, responder));

    public void OnPost(string pathContains, string jsonBody, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes.Add(new Route(HttpMethod.Post, pathContains, _ => jsonBody, status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        RequestBodies.Add(body);

        var route = _routes.FirstOrDefault(r => r.Method == request.Method
            && request.RequestUri!.AbsolutePath.Contains(r.PathContains, StringComparison.OrdinalIgnoreCase));

        if (route is null)
        {
            throw new InvalidOperationException($"no stub for {request.Method} {request.RequestUri}");
        }

        if (route.Dynamic is not null)
        {
            var (dynamicStatus, dynamicBody) = route.Dynamic(request);
            return new HttpResponseMessage(dynamicStatus)
            {
                Content = new StringContent(dynamicBody, Encoding.UTF8, "application/json"),
            };
        }

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body(request), Encoding.UTF8, "application/json"),
        };
    }
}
