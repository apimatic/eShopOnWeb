using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PayPal;

/// <summary>
/// Test seam for the PayPal SDK client: a fake transport that answers the OAuth token request with a
/// canned token and every other request with a configured status + body. Captures requests so tests
/// can assert what was sent and how many times.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public List<HttpRequestMessage> Requests { get; } = new();
    public int NonTokenRequestCount { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.Contains("oauth2/token", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Json(HttpStatusCode.OK,
                "{\"access_token\":\"fake-token\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"scope\":\"\"}"));
        }

        NonTokenRequestCount++;
        return Task.FromResult(Json(_status, _body));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };
}
