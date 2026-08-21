using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Exercises the gateway's translation of SDK responses and errors using the SDK's own test seam:
/// an <see cref="HttpClient"/> backed by a stub handler, so no real network calls happen.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static PayPalPaymentGateway GatewayReturning(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new PayPalServerSdkClient(new HttpClient(new StubHandler(responder)),
            new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
            });
        return new PayPalPaymentGateway(client, NullLogger<PayPalPaymentGateway>.Instance);
    }

    private static HttpResponseMessage TokenResponse() =>
        Json(HttpStatusCode.OK, """{ "access_token": "test-token", "token_type": "Bearer", "expires_in": 3600 }""");

    private static PayPalAuthorizationRequest AuthRequest() => new()
    {
        Amount = 39m,
        CurrencyCode = "USD",
        OrderReference = 1,
        InvoiceReference = "inv-token",
        IdempotencyKey = "key-1",
        Card = new PayPalCardDetails { Number = "4111111111111111", Expiry = "2030-12", SecurityCode = "123" }
    };

    [Fact]
    public async Task Authorize_WhenPayPalReturnsValidationError_ThrowsPaymentGatewayException()
    {
        var gateway = GatewayReturning(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return TokenResponse();

            // 422 with the typed Error shape CreateOrder maps to.
            return Json((HttpStatusCode)422, """
            {
              "name": "UNPROCESSABLE_ENTITY",
              "message": "The requested action could not be performed.",
              "debug_id": "abc123",
              "details": [ { "issue": "DUPLICATE_INVOICE_ID", "description": "Duplicate Invoice ID detected." } ]
            }
            """);
        });

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(AuthRequest()));
        Assert.Contains("DUPLICATE_INVOICE_ID", ex.Message);
    }

    [Fact]
    public async Task Authorize_WhenChallengeRequired_ReturnsRequiresAction()
    {
        var gateway = GatewayReturning(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return TokenResponse();

            // Order created but needs buyer approval (3-D Secure): PAYER_ACTION_REQUIRED, no authorization.
            return Json(HttpStatusCode.Created, """
            {
              "id": "PPORDER1",
              "status": "PAYER_ACTION_REQUIRED",
              "purchase_units": []
            }
            """);
        });

        var result = await gateway.AuthorizeAsync(AuthRequest());

        Assert.True(result.RequiresAction);
        Assert.Equal("PPORDER1", result.PayPalOrderId);
        Assert.Null(result.AuthorizationId);
    }

    [Fact]
    public async Task Authorize_WhenOrderAuthorizedOnCreate_ReturnsAuthorization()
    {
        var gateway = GatewayReturning(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("oauth2/token"))
                return TokenResponse();

            // Advanced card processing produced the authorization directly on the create response.
            return Json(HttpStatusCode.Created, """
            {
              "id": "PPORDER1",
              "status": "COMPLETED",
              "purchase_units": [
                {
                  "payments": {
                    "authorizations": [
                      { "id": "AUTH-XYZ", "status": "CREATED", "expiration_time": "2030-01-01T00:00:00Z" }
                    ]
                  }
                }
              ]
            }
            """);
        });

        var result = await gateway.AuthorizeAsync(AuthRequest());

        Assert.False(result.RequiresAction);
        Assert.Equal("PPORDER1", result.PayPalOrderId);
        Assert.Equal("AUTH-XYZ", result.AuthorizationId);
        Assert.Equal("CREATED", result.AuthorizationStatus);
    }
}
