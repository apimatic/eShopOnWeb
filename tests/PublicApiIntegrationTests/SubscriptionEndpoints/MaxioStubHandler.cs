using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

public record CapturedRequest(HttpMethod Method, Uri? Uri, string? Body);

/// <summary>
/// HTTP-level fake for the Maxio SDK: the SDK client takes an HttpClient, so tests
/// substitute this handler on the named "Maxio" client and no network calls happen.
/// Captured requests (not invocations — retries append) are what assertions count.
/// </summary>
public class MaxioStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<CapturedRequest> Requests { get; } = new();

    public MaxioStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
        return _responder(request);
    }

    public int CountRequests(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && string.Equals(r.Uri?.AbsolutePath, path, StringComparison.OrdinalIgnoreCase));

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
