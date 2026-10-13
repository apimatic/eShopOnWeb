using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using WalmartApIs;
using WalmartApIs.Core.Authentication.OAuth2.ClientCredentials;
using WalmartApIs.Core.Configuration;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Marketplace;

/// <summary>
/// A fake HttpMessageHandler — the SDK's test seam. It answers the WalletAuth token request itself
/// and hands back scripted responses (or a thrown transport fault) for everything else, recording
/// each outgoing request and its body for assertions.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;
    public List<Uri> RequestUris { get; } = new();
    public List<string?> Bodies { get; } = new();
    public List<Uri> TokenRequestUris { get; } = new();

    public StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> Json(HttpStatusCode status, string json) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static Func<HttpRequestMessage, HttpResponseMessage> Throw() =>
        _ => throw new HttpRequestException("connection reset");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // The WalletAuth token endpoint is POST {base}/auth/token — answer it ourselves.
        if (request.RequestUri!.AbsolutePath.EndsWith("/auth/token", StringComparison.Ordinal))
        {
            TokenRequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""",
                    Encoding.UTF8, "application/json")
            });
        }

        RequestUris.Add(request.RequestUri!);
        Bodies.Add(request.Content?.ReadAsStringAsync(ct).Result);

        var responder = _responders.Count > 1 ? _responders.Dequeue() : _responders.Peek();
        var response = responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>No-op logger so the service under test needs no logging infrastructure.</summary>
public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
}

internal static class WalmartClientFactory
{
    public static WalmartApIsClient Create(StubHandler handler, string? baseUrlOverride = null)
    {
        var options = new WalmartApIsClientOptions
        {
            WalletAuth = new OAuth2ClientCredentials { ClientId = "cid", ClientSecret = "secret" },
            // Disable retries so transport-fault and error tests are fast and deterministic.
            Retry = RetryOptions.Default() with { MaxRetries = 0 }
        };

        if (baseUrlOverride is not null)
        {
            var overrideUri = new Uri(baseUrlOverride, UriKind.Absolute);
            options.Server.Default.Production.BaseUrl = Rebase(options.Server.Default.Production.BaseUrl, overrideUri);
            options.Server.Default1.Production.BaseUrl = Rebase(options.Server.Default1.Production.BaseUrl, overrideUri);
        }

        return new WalmartApIsClient(new HttpClient(handler), options);
    }

    private static string Rebase(string originalUrl, Uri overrideUri)
    {
        var original = new Uri(originalUrl, UriKind.Absolute);
        return $"{overrideUri.Scheme}://{overrideUri.Authority}{original.AbsolutePath}".TrimEnd('/');
    }
}
