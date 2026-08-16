using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Test double for <see cref="HttpMessageHandler"/> that records every request (method, path, query,
/// body) and returns a scripted response chosen by the supplied responder. Lets the Maxio service be
/// exercised end-to-end without a live API.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, (HttpStatusCode Status, string Json)> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));

        var (status, json) = _responder(request, body);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public int CountOf(HttpMethod method, string absolutePath) =>
        Requests.FindAll(r => r.Method == method && r.Path == absolutePath).Count;

    internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Body);
}
