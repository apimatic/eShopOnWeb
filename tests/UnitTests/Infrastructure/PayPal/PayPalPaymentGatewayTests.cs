using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.PayPal;

/// <summary>
/// Exercises the transport seam (the SDK's <see cref="HttpClient"/> constructor arg) — specifically the
/// unknown-outcome settle: an idempotent write that fails on transport once is resent under the same
/// PayPal-Request-Id, and a persistent transport failure surfaces as an unknown outcome rather than a plain
/// failure. These paths cannot be induced against the live sandbox, so they are covered here.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private const string TokenJson = """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""";

    private const string OrderJson = """
    {"id":"O-TEST-1","status":"COMPLETED","purchase_units":[
      {"payments":{"authorizations":[{"id":"AUTH-1","status":"CREATED","amount":{"currency_code":"USD","value":"29.00"}}]}}],
      "payment_source":{"card":{"brand":"VISA","last_digits":"1111"}}}
    """;

    private static PayPalPaymentGateway BuildGateway(HttpMessageHandler handler)
    {
        var client = new global::PayPalServerSdk.PayPalServerSdkClient(
            new HttpClient(handler),
            new global::PayPalServerSdk.PayPalServerSdkClientOptions
            {
                Oauth2 = new global::PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
                {
                    ClientId = "id",
                    ClientSecret = "secret"
                }
            });

        var options = Options.Create(new PayPalOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            CallTimeoutSeconds = 10
        });

        return new PayPalPaymentGateway(client, options, NullLogger<PayPalPaymentGateway>.Instance);
    }

    private static PayPalAuthorizeRequest AuthorizeRequest() => new()
    {
        Amount = 29.00m,
        Currency = "USD",
        InvoiceId = "ESHOP-1-abc",
        CustomId = "1",
        RequestId = "auth-1-fixed",
        Card = new CardDetails { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" }
    };

    [Fact]
    public async Task Authorize_resends_once_on_transport_failure_and_settles()
    {
        var createOrderAttempts = 0;
        var handler = new RoutingStubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return Respond(HttpStatusCode.OK, TokenJson);

            if (request.RequestUri.AbsolutePath.Contains("/v2/checkout/orders"))
            {
                createOrderAttempts++;
                if (createOrderAttempts == 1)
                    throw new HttpRequestException("connection reset"); // transport failure on first attempt
                return Respond(HttpStatusCode.Created, OrderJson);
            }

            return Respond(HttpStatusCode.OK, "{}");
        });

        var gateway = BuildGateway(handler);

        var result = await gateway.AuthorizeAsync(AuthorizeRequest(), CancellationToken.None);

        Assert.Equal("AUTH-1", result.AuthorizationId);
        Assert.Equal("O-TEST-1", result.PayPalOrderId);
        Assert.Equal(2, createOrderAttempts); // resent exactly once under the same request id
    }

    [Fact]
    public async Task Authorize_reports_unknown_outcome_when_transport_keeps_failing()
    {
        var handler = new RoutingStubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return Respond(HttpStatusCode.OK, TokenJson);
            throw new HttpRequestException("connection reset");
        });

        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<PayPalGatewayException>(
            () => gateway.AuthorizeAsync(AuthorizeRequest(), CancellationToken.None));

        Assert.True(ex.OutcomeUnknown);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class RoutingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public RoutingStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
