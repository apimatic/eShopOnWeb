using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Sms;

/// <summary>
/// The test seam for the Twilio SDK: an <see cref="HttpMessageHandler"/> that answers from a
/// caller-supplied responder and records every outgoing request so tests can assert what actually
/// went on the wire (no real network calls).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> RequestBodies { get; } = new();
    public HttpRequestMessage LastRequest => Requests[^1];
    public string LastBody => RequestBodies[^1];

    public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, json) = _responder(request);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
