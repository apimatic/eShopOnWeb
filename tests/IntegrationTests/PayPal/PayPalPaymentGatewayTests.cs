using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Tests the gateway against the SDK's HttpClient seam (no real network). The stub answers the
/// OAuth token request and then the operation, so we assert real behaviour, not execution.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _operation;
        public List<string?> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> operation) => _operation = operation;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : request.Content.ReadAsStringAsync().Result;
            Bodies.Add(body);

            if (request.RequestUri!.AbsolutePath.Contains("oauth2/token"))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"test-token","token_type":"Bearer","expires_in":3600}""",
                        System.Text.Encoding.UTF8, "application/json")
                };
                resp.RequestMessage = request;
                return Task.FromResult(resp);
            }

            var operationResponse = _operation(request);
            operationResponse.RequestMessage = request;
            return Task.FromResult(operationResponse);
        }
    }

    private static PayPalPaymentGateway GatewayReturning(Func<HttpRequestMessage, HttpResponseMessage> operation, out StubHandler handler)
    {
        handler = new StubHandler(operation);
        var options = new PayPalServerSdkClientOptions
        {
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        };
        var client = new PayPalServerSdkClient(new HttpClient(handler), options);
        return new PayPalPaymentGateway(client, NullLogger<PayPalPaymentGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AuthorizeAsync_ParsesAuthorization_AndSendsAuthorizeIntentAndAmount()
    {
        const string orderJson = """
        {
          "id": "PPORDER123",
          "status": "COMPLETED",
          "purchase_units": [
            { "payments": { "authorizations": [ { "id": "AUTH123", "status": "CREATED", "expiration_time": "2027-01-01T00:00:00Z" } ] } }
          ]
        }
        """;
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, orderJson), out var handler);

        var result = await gateway.AuthorizeAsync(new AuthorizeCommand
        {
            OrderId = 1,
            Amount = 39.00m,
            CurrencyCode = "USD",
            ReconciliationReference = "ESHOP-abc",
            IdempotencyKey = "key-1",
            Card = new CardDetails { Number = "4111111111111111", Expiry = "2027-01", SecurityCode = "123", BillingCountryCode = "US" }
        }, CancellationToken.None);

        Assert.Equal("PPORDER123", result.PayPalOrderId);
        Assert.Equal("AUTH123", result.AuthorizationId);
        Assert.Equal("CREATED", result.AuthorizationStatus);

        var sent = handler.Bodies[^1]!;
        Assert.Contains("\"intent\":\"AUTHORIZE\"", sent);
        Assert.Contains("\"value\":\"39.00\"", sent);      // amount to the cent
        Assert.Contains("ESHOP-abc", sent);                // reconciliation reference (custom_id/invoice_id)
    }

    [Fact]
    public async Task AuthorizeAsync_PayerActionRequired_ReportsChallenge()
    {
        const string orderJson = """{ "id": "PPO", "status": "PAYER_ACTION_REQUIRED", "purchase_units": [] }""";
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, orderJson), out _);

        await Assert.ThrowsAsync<PaymentChallengeRequiredException>(() =>
            gateway.AuthorizeAsync(new AuthorizeCommand
            {
                OrderId = 1, Amount = 5m, CurrencyCode = "USD", ReconciliationReference = "ESHOP-x", IdempotencyKey = "k",
                Card = new CardDetails { Number = "4111111111111111", Expiry = "2027-01" }
            }, CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeAsync_ProviderRejects_TranslatesToCallerError()
    {
        const string errorJson = """
        { "name": "UNPROCESSABLE_ENTITY", "message": "bad", "debug_id": "dbg-1",
          "details": [ { "issue": "INSTRUMENT_DECLINED", "description": "declined" } ] }
        """;
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.UnprocessableEntity, errorJson), out _);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            gateway.AuthorizeAsync(new AuthorizeCommand
            {
                OrderId = 1, Amount = 5m, CurrencyCode = "USD", ReconciliationReference = "ESHOP-x", IdempotencyKey = "k",
                Card = new CardDetails { Number = "4111111111111111", Expiry = "2027-01" }
            }, CancellationToken.None));

        Assert.Equal(PaymentGatewayFailureKind.CallerError, ex.Kind);
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("UNPROCESSABLE_ENTITY", ex.ProviderErrorName);
        Assert.Equal("dbg-1", ex.DebugId);
    }

    [Fact]
    public async Task VoidAsync_204NoContent_IsTreatedAsSuccess()
    {
        var gateway = GatewayReturning(_ => new HttpResponseMessage(HttpStatusCode.NoContent), out _);

        // Should not throw despite the empty (non-deserializable) body.
        await gateway.VoidAsync("AUTH123", "void-key", CancellationToken.None);
    }

    [Fact]
    public async Task CaptureAsync_ConnectionFailure_MapsToUnknownOutcome()
    {
        // The stub answers the token request, then throws on the operation — a transport failure
        // after the provider may have acted. The gateway must classify this as Unknown (not failed).
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var options = new PayPalServerSdkClientOptions { Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "s" } };
        var gateway = new PayPalPaymentGateway(new PayPalServerSdkClient(new HttpClient(handler), options), NullLogger<PayPalPaymentGateway>.Instance);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            gateway.CaptureAsync("AUTH123", 10m, "USD", "cap-key", CancellationToken.None));

        Assert.Equal(PaymentGatewayFailureKind.Unknown, ex.Kind);
    }

    [Fact]
    public async Task CaptureAsync_ParsesSellerReceivableBreakdown()
    {
        const string captureJson = """
        {
          "id": "CAP123", "status": "COMPLETED",
          "amount": { "currency_code": "USD", "value": "39.00" },
          "seller_receivable_breakdown": {
            "gross_amount": { "currency_code": "USD", "value": "39.00" },
            "paypal_fee": { "currency_code": "USD", "value": "1.50" },
            "net_amount": { "currency_code": "USD", "value": "37.50" }
          }
        }
        """;
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, captureJson), out _);

        var result = await gateway.CaptureAsync("AUTH123", 39.00m, "USD", "cap-key", CancellationToken.None);

        Assert.Equal("CAP123", result.CaptureId);
        Assert.Equal(39.00m, result.CapturedAmount);
        Assert.Equal(1.50m, result.PayPalFee);
        Assert.Equal(37.50m, result.NetAmount);
    }
}
