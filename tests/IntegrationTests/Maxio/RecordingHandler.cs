using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that records every request (method, URI, body) and replies
/// via a supplied responder. This is the SDK's testing seam: the Maxio client is built over an
/// <see cref="HttpClient"/> wrapping this handler, so no real network calls happen and retries append
/// to <see cref="Requests"/> where they can be counted.
/// </summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public List<RecordedRequest> Requests { get; } = new();

    public RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
        return _responder(request, body);
    }

    public static HttpResponseMessage Json(System.Net.HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
