using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Walmart;

/// <summary>
/// A test HttpMessageHandler that records every request and returns scripted responses, so the Walmart
/// client can be exercised against the contract without a live Walmart environment.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            request.Content?.Headers.ContentType?.MediaType));

        return _responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    public record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string Body,
        string? AuthScheme,
        string? AuthParameter,
        string? ContentType);
}
