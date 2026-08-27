using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class PayPalPaymentGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        // Bodies are captured at send time — the SDK disposes request content afterwards.
        public List<string?> Bodies { get; } = new();
        public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string TokenResponse =
        """{"access_token":"test-token","token_type":"Bearer","expires_in":3600,"scope":"test","nonce":"n"}""";

    private static PayPalPaymentGateway GatewayReturning(Func<HttpRequestMessage, HttpResponseMessage> responder,
        out StubHandler handler)
    {
        handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("oauth2/token")
                ? Json(HttpStatusCode.OK, TokenResponse)
                : responder(request));

        var client = new PayPalServerSdkClient(new HttpClient(handler), new PayPalServerSdkClientOptions
        {
            Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "id", ClientSecret = "secret" }
        });
        return new PayPalPaymentGateway(client);
    }

    private static readonly CardPaymentDetails TestCard = new CardPaymentDetails(
        Number: "4111111111111111",
        Expiry: "2030-12",
        SecurityCode: "123",
        CardholderName: "Test Buyer",
        BillingAddress: new GatewayAddress("US", "1 Main St", null, "Seattle", "WA", "98101"));

    [Fact]
    public async Task AuthorizeCardPaymentMapsAuthorization()
    {
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, """
            {
              "id": "PP-ORDER-1",
              "status": "COMPLETED",
              "purchase_units": [
                {
                  "reference_id": "eshop-order-1",
                  "payments": {
                    "authorizations": [
                      {
                        "id": "AUTH-1",
                        "status": "CREATED",
                        "amount": { "currency_code": "USD", "value": "51.00" },
                        "expiration_time": "2026-09-25T10:00:00Z"
                      }
                    ]
                  }
                }
              ]
            }
            """), out var handler);

        var result = await gateway.AuthorizeCardPaymentAsync(51.00m, "USD", "eshop-order-1", TestCard, "key-1");

        Assert.Equal("PP-ORDER-1", result.PayPalOrderId);
        Assert.Equal("AUTH-1", result.AuthorizationId);
        Assert.Equal("CREATED", result.Status);
        Assert.Equal(51.00m, result.Amount);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero), result.ExpiresAt);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/v2/checkout/orders", request.RequestUri!.AbsolutePath);
        Assert.Equal("key-1", request.Headers.GetValues("PayPal-Request-Id").Single());

        var sentJson = handler.LastBody!;
        Assert.Contains("\"intent\":\"AUTHORIZE\"", sentJson);
        Assert.Contains("\"value\":\"51.00\"", sentJson);
        Assert.Contains("\"currency_code\":\"USD\"", sentJson);
    }

    [Fact]
    public async Task AuthorizeCardPaymentTranslatesApiError()
    {
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.UnprocessableEntity, """
            {
              "name": "UNPROCESSABLE_ENTITY",
              "message": "The requested action could not be performed.",
              "debug_id": "abc123",
              "details": [ { "issue": "DUPLICATE_INVOICE_ID", "description": "Duplicate invoice." } ]
            }
            """), out _);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(
            () => gateway.AuthorizeCardPaymentAsync(51.00m, "USD", "eshop-order-1", TestCard, "key-1"));

        Assert.Contains("UNPROCESSABLE_ENTITY", ex.Message);
        Assert.Contains("DUPLICATE_INVOICE_ID", ex.Message);
        Assert.Contains("abc123", ex.Message);
    }

    [Fact]
    public async Task AuthorizeCardPaymentStopsOnPayerAction()
    {
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, """
            {
              "id": "PP-ORDER-1",
              "status": "PAYER_ACTION_REQUIRED",
              "purchase_units": []
            }
            """), out _);

        await Assert.ThrowsAsync<PayerActionRequiredException>(
            () => gateway.AuthorizeCardPaymentAsync(51.00m, "USD", "eshop-order-1", TestCard, "key-1"));
    }

    [Fact]
    public async Task CaptureReturnsFeeBreakdown()
    {
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.Created, """
            {
              "id": "CAP-1",
              "status": "COMPLETED",
              "amount": { "currency_code": "USD", "value": "51.00" },
              "final_capture": true,
              "seller_receivable_breakdown": {
                "gross_amount": { "currency_code": "USD", "value": "51.00" },
                "paypal_fee": { "currency_code": "USD", "value": "1.81" },
                "net_amount": { "currency_code": "USD", "value": "49.19" }
              }
            }
            """), out var handler);

        var capture = await gateway.CaptureAuthorizationAsync("AUTH-1", "key-2");

        Assert.Equal("CAP-1", capture.CaptureId);
        Assert.Equal("COMPLETED", capture.Status);
        Assert.Equal(51.00m, capture.Amount);
        Assert.Equal(1.81m, capture.PayPalFee);
        Assert.Equal(49.19m, capture.NetAmount);
        Assert.Equal("key-2", handler.LastRequest!.Headers.GetValues("PayPal-Request-Id").Single());
    }

    [Fact]
    public async Task ReauthorizeRejectionBecomesOperatorActionableConflict()
    {
        var gateway = GatewayReturning(_ => Json(HttpStatusCode.UnprocessableEntity, """
            {
              "name": "UNPROCESSABLE_ENTITY",
              "message": "Authorization cannot be reauthorized.",
              "debug_id": "def456",
              "details": [ { "issue": "CANNOT_REAUTHORIZE", "description": "Too old." } ]
            }
            """), out _);

        var ex = await Assert.ThrowsAsync<PaymentStateException>(
            () => gateway.ReauthorizeAsync("AUTH-1", 51.00m, "USD", "key-3"));

        Assert.Contains("can no longer be renewed", ex.Message);
        Assert.Contains("CANNOT_REAUTHORIZE", ex.Message);
    }

    [Fact]
    public async Task SearchTransactionsPagesThroughWholeRange()
    {
        var page2Requested = false;
        var gateway = GatewayReturning(request =>
        {
            var query = request.RequestUri!.Query;
            if (query.Contains("page=2"))
            {
                page2Requested = true;
                return Json(HttpStatusCode.OK, """
                    {
                      "transaction_details": [
                        { "transaction_info": { "transaction_id": "T-2", "transaction_status": "S",
                          "transaction_amount": { "currency_code": "USD", "value": "5.00" } } }
                      ],
                      "page": 2, "total_items": 2, "total_pages": 2
                    }
                    """);
            }

            return Json(HttpStatusCode.OK, """
                {
                  "transaction_details": [
                    { "transaction_info": { "transaction_id": "T-1", "transaction_status": "S",
                      "transaction_amount": { "currency_code": "USD", "value": "10.00" } } }
                  ],
                  "page": 1, "total_items": 2, "total_pages": 2
                }
                """);
        }, out _);

        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var transactions = await gateway.SearchTransactionsAsync(from, to);

        Assert.True(page2Requested);
        Assert.Equal(new[] { "T-1", "T-2" }, transactions.Select(t => t.TransactionId).ToArray());
    }

    [Fact]
    public async Task SearchTransactionsChunksRangesBeyond31Days()
    {
        var windows = new List<string>();
        var gateway = GatewayReturning(request =>
        {
            windows.Add(request.RequestUri!.Query);
            return Json(HttpStatusCode.OK, """{ "transaction_details": [], "page": 1, "total_items": 0, "total_pages": 1 }""");
        }, out _);

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        await gateway.SearchTransactionsAsync(from, to);

        Assert.Equal(3, windows.Count);
        Assert.Contains("start_date=2026-01-01", windows[0]);
        Assert.Contains("start_date=2026-02-01", windows[1]);
        Assert.Contains("start_date=2026-03-04", windows[2]);
    }
}
