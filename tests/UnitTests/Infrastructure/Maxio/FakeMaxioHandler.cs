using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Routes HttpClient calls to canned JSON responses, and records every request so tests can assert
/// on how many times (and in what order) the Maxio API was actually called.
/// </summary>
public class FakeMaxioHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = new();

    public List<(HttpMethod Method, string PathAndQuery)> Requests { get; } = new();

    public FakeMaxioHandler When(HttpMethod method, string pathPrefix, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((req => req.Method == method && req.RequestUri!.AbsolutePath.TrimStart('/').StartsWith(pathPrefix, StringComparison.Ordinal), respond));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.PathAndQuery));

        foreach (var (match, respond) in _routes)
        {
            if (match(request))
            {
                return Task.FromResult(respond(request));
            }
        }

        throw new InvalidOperationException($"No fake route registered for {request.Method} {request.RequestUri}");
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}
