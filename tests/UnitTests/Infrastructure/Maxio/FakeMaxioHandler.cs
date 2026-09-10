using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// A scriptable <see cref="HttpMessageHandler"/> for exercising the Maxio HTTP client without a
/// network. Routing is delegated to a caller-supplied function keyed by method + path, and every
/// request is recorded for assertions.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, (HttpStatusCode Status, string Json)> _router;

    public FakeMaxioHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> router)
    {
        _router = router;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        Requests.Add(new RecordedRequest(request.Method, pathAndQuery, body));

        var (status, json) = _router(request, pathAndQuery);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    public sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string Body);
}
