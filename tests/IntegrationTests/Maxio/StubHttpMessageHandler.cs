using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that records outgoing requests and
/// produces responses from a caller-supplied responder. Lets us drive
/// <c>MaxioBillingService</c> through its HTTP contract without any network access.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body);
        Requests.Add(recorded);
        return _responder(recorded);
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body);
