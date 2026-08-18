using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

/// <summary>
/// A test seam for the Twilio SDK: the client takes an <see cref="HttpClient"/>, so backing it with
/// this handler lets us assert the outgoing request and control the response with no real network.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public string? LastBody => RequestBodies.Count == 0 ? null : RequestBodies[^1];

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public static StubHttpMessageHandler Json(HttpStatusCode status, string json) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}
