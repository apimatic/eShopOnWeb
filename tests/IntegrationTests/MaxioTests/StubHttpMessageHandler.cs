using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.MaxioTests;

/// <summary>
/// A test double for <see cref="HttpMessageHandler"/> that routes requests to canned JSON
/// responses and records every request, so we can assert on what the Maxio client sent.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, (HttpStatusCode Status, string Json)> _router;

    public StubHttpMessageHandler(Func<RecordedRequest, (HttpStatusCode Status, string Json)> router)
    {
        _router = router;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri!.Query,
            body);
        Requests.Add(recorded);

        var (status, json) = _router(recorded);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    public sealed record RecordedRequest(string Method, string Path, string Query, string Body);
}
