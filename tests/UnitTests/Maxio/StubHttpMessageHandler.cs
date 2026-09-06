using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>Records the requests made and replies with canned responses.</summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public static StubHttpMessageHandler Returning(HttpStatusCode statusCode, string json) =>
        new((_, _) => Json(statusCode, json));

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Add(request);

        return _responder(request, Requests.Count);
    }
}
