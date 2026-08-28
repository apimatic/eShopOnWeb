using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Core.Configuration;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.PayPal;

/// <summary>
/// The SDK client takes an <see cref="HttpClient"/>, which is the seam these tests use: no network,
/// no PayPal account, and the outgoing request is observable.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
    private int _calls;

    public StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) => _responder = responder;

    public StubHandler(HttpStatusCode status, string json)
        : this((_, _) => Json(status, json)) { }

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// Buffered during the send: the SDK disposes request content per attempt, so reading the body
    /// off a captured request afterwards throws.
    /// </summary>
    public List<string?> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        var response = _responder(request, ++_calls);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public static class GatewayFactory
{
    public static PayPalPaymentGateway Create(StubHandler handler, string currency = "USD")
    {
        // No credentials configured, so no token round-trip: the stub answers the operation directly.
        var client = new PayPalClient(new HttpClient(handler), new PayPalClientOptions
        {
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(5) },
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
        });

        var settings = Options.Create(new PayPalSettings
        {
            ClientId = "id",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = currency
        });

        return new PayPalPaymentGateway(client, settings, NullLogger<PayPalPaymentGateway>.Instance);
    }
}
