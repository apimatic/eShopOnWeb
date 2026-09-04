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
using NSubstitute;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class PayPalGatewayTests
{
    public sealed class CapturedRequest
    {
        public HttpRequestMessage Request { get; }
        public string Body { get; }

        public CapturedRequest(HttpRequestMessage request, string body)
        {
            Request = request;
            Body = body;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            Requests.Add(new CapturedRequest(request, body));
            return _responder(request);
        }
    }

    private static PayPalServerSdkClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHandler handler)
    {
        handler = new StubHandler(responder);
        var httpClient = new HttpClient(handler);
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "test-client", ClientSecret = "test-secret" }
        };
        return new PayPalServerSdkClient(httpClient, options);
    }

    private static PayPalGateway Gateway(Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHandler handler)
    {
        return new PayPalGateway(Client(responder, out handler), Substitute.For<IAppLogger<PayPalGateway>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Responder(string operationPath, string operationJson)
    {
        return request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
            {
                return Json(HttpStatusCode.OK, """{"access_token":"TEST-TOKEN","expires_in":3600,"token_type":"Bearer"}""");
            }

            if (request.RequestUri!.AbsolutePath == operationPath)
            {
                return Json(HttpStatusCode.OK, operationJson);
            }

            return Json(HttpStatusCode.NotFound, """{"name":"NOT_FOUND","message":"not found","debug_id":"x"}""");
        };
    }

    private static PayPalCardDetails TestCard()
    {
        return new PayPalCardDetails(
            "Test Shopper",
            "4111111111111111",
            "2028-09",
            "123",
            new PayPalCardAddress("1 Main St", null, "Seattle", "WA", "98101", "US"));
    }

    [Fact]
    public async Task CreateOrderAsyncSendsAuthorizeIntentAndStableRequestId()
    {
        var gateway = Gateway(
            Responder("/v2/checkout/orders", """
                {
                  "id": "ORDER-123",
                  "status": "COMPLETED",
                  "purchase_units": [
                    {
                      "reference_id": "42",
                      "payments": {
                        "authorizations": [
                          { "id": "AUTH-1", "status": "CREATED", "expiration_time": "2026-09-08T00:00:00Z" }
                        ]
                      }
                    }
                  ],
                  "links": []
                }
                """),
            out var handler);

        var result = await gateway.CreateOrderAsync(42, 19.50m, "USD", TestCard(), null, "eshop-pay-42", CancellationToken.None);

        Assert.Equal("ORDER-123", result.OrderId);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("AUTH-1", result.AuthorizationId);
        Assert.Equal("CREATED", result.AuthorizationStatus);
        Assert.NotNull(result.ExpirationTime);

        var request = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath == "/v2/checkout/orders");
        Assert.Equal(HttpMethod.Post, request.Request.Method);
        Assert.Equal("eshop-pay-42", request.Request.Headers.GetValues("PayPal-Request-Id").Single());

        Assert.Contains("\"intent\":\"AUTHORIZE\"", request.Body);
        Assert.Contains("\"reference_id\":\"42\"", request.Body);
        Assert.Contains("\"value\":\"19.50\"", request.Body);
        Assert.Contains("\"currency_code\":\"USD\"", request.Body);
    }

    [Fact]
    public async Task CreateOrderAsyncSendsVaultIdForSavedCard()
    {
        var gateway = Gateway(
            Responder("/v2/checkout/orders", """{"id":"ORDER-123","status":"CREATED","links":[]}"""),
            out var handler);

        await gateway.CreateOrderAsync(42, 10.00m, "USD", null, "VAULT-1", "req", CancellationToken.None);

        var request = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath == "/v2/checkout/orders");
        Assert.Contains("\"vault_id\":\"VAULT-1\"", request.Body);
        Assert.DoesNotContain("\"number\"", request.Body);
    }

    [Fact]
    public async Task AuthorizeOrderAsyncReadsAuthorizationFromPurchaseUnits()
    {
        var gateway = Gateway(
            Responder("/v2/checkout/orders/ORDER-123/authorize", """
                {
                  "id": "ORDER-123",
                  "status": "COMPLETED",
                  "purchase_units": [
                    {
                      "reference_id": "42",
                      "payments": {
                        "authorizations": [
                          { "id": "AUTH-1", "status": "AUTHORIZED", "expiration_time": "2026-09-08T00:00:00Z" }
                        ]
                      }
                    }
                  ],
                  "links": []
                }
                """),
            out _);

        var result = await gateway.AuthorizeOrderAsync("ORDER-123", TestCard(), null, "req", CancellationToken.None);

        Assert.Equal("AUTH-1", result.AuthorizationId);
        Assert.Equal("AUTHORIZED", result.AuthorizationStatus);
        Assert.Equal("COMPLETED", result.OrderStatus);
        Assert.NotNull(result.ExpirationTime);
    }

    [Fact]
    public async Task CaptureAsyncReadsSellerReceivableBreakdown()
    {
        var gateway = Gateway(
            Responder("/v2/payments/authorizations/AUTH-1/capture", """
                {
                  "id": "CAP-1",
                  "status": "COMPLETED",
                  "amount": { "currency_code": "USD", "value": "19.50" },
                  "seller_receivable_breakdown": {
                    "gross_amount": { "currency_code": "USD", "value": "19.50" },
                    "paypal_fee": { "currency_code": "USD", "value": "0.93" },
                    "net_amount": { "currency_code": "USD", "value": "18.57" }
                  }
                }
                """),
            out _);

        var result = await gateway.CaptureAsync("AUTH-1", "eshop-capture-42", CancellationToken.None);

        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(19.50m, result.GrossAmount);
        Assert.Equal(0.93m, result.FeeAmount);
        Assert.Equal(18.57m, result.NetAmount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task RefundAsyncSendsCallerIdempotencyKeyAsRequestIdHeader()
    {
        var gateway = Gateway(
            Responder("/v2/payments/captures/CAP-1/refund", """{"id":"REF-1","status":"COMPLETED","amount":{"currency_code":"USD","value":"5.00"}}"""),
            out var handler);

        var result = await gateway.RefundAsync("CAP-1", 5.00m, "USD", "caller-key-1", CancellationToken.None);

        Assert.Equal("REF-1", result.RefundId);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(5.00m, result.Amount);

        var request = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath == "/v2/payments/captures/CAP-1/refund");
        Assert.Equal("caller-key-1", request.Request.Headers.GetValues("PayPal-Request-Id").Single());
        Assert.Contains("\"value\":\"5.00\"", request.Body);
    }

    [Fact]
    public async Task CreatePaymentTokenAsyncReadsSafeDisplayData()
    {
        var gateway = Gateway(
            Responder("/v3/vault/payment-tokens", """
                {
                  "id": "TOKEN-1",
                  "customer": { "merchant_customer_id": "buyer@example.com" },
                  "payment_source": {
                    "card": { "last_digits": "1111", "brand": "VISA", "expiry": "2028-09" }
                  },
                  "links": []
                }
                """),
            out _);

        var result = await gateway.CreatePaymentTokenAsync(TestCard(), "req", "buyer@example.com", CancellationToken.None);

        Assert.Equal("TOKEN-1", result.TokenId);
        Assert.Equal("1111", result.Last4);
        Assert.Equal("VISA", result.Brand);
        Assert.Equal("2028-09", result.Expiry);
    }

    [Fact]
    public async Task SearchTransactionsAsyncParsesTransactionRecords()
    {
        var gateway = Gateway(
            Responder("/v1/reporting/transactions", """
                {
                  "total_items": 1,
                  "total_pages": 1,
                  "page": 1,
                  "transaction_details": [
                    {
                      "transaction_info": {
                        "transaction_id": "TXN-1",
                        "paypal_reference_id": "42",
                        "transaction_event_code": "T0001",
                        "transaction_status": "COMPLETED",
                        "transaction_initiation_date": "2026-09-04T10:00:00Z",
                        "transaction_amount": { "currency_code": "USD", "value": "19.50" },
                        "fee_amount": { "currency_code": "USD", "value": "0.93" }
                      },
                      "payer_info": { "email_address": "buyer@example.com" }
                    }
                  ]
                }
                """),
            out var handler);

        var results = await gateway.SearchTransactionsAsync(
            DateTimeOffset.Parse("2026-09-04T00:00:00Z"), DateTimeOffset.Parse("2026-09-05T00:00:00Z"), CancellationToken.None);

        var record = Assert.Single(results);
        Assert.Equal("TXN-1", record.TransactionId);
        Assert.Equal("42", record.ReferenceId);
        Assert.Equal("T0001", record.EventCode);
        Assert.Equal(19.50m, record.Amount);
        Assert.Equal(0.93m, record.Fee);
        Assert.Equal("buyer@example.com", record.PayerEmail);

        var request = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath == "/v1/reporting/transactions");
        Assert.Contains("start_date=", request.Request.RequestUri!.Query);
        Assert.Contains("page_size=100", request.Request.RequestUri!.Query);
    }

    [Fact]
    public async Task ProviderRejectionIsMappedToPayPalApiExceptionWithStatus()
    {
        var gateway = Gateway(
            request =>
            {
                if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
                {
                    return Json(HttpStatusCode.OK, """{"access_token":"TEST-TOKEN","expires_in":3600,"token_type":"Bearer"}""");
                }

                return Json(HttpStatusCode.UnprocessableEntity, """
                    {
                      "name": "UNPROCESSABLE_ENTITY",
                      "message": "The requested action could not be performed",
                      "details": [ { "issue": "INSTRUMENT_DECLINED", "description": "declined" } ],
                      "debug_id": "dbg-1"
                    }
                    """);
            },
            out _);

        var ex = await Assert.ThrowsAsync<PayPalApiException>(() =>
            gateway.CreateOrderAsync(42, 10.00m, "USD", TestCard(), null, "req", CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("INSTRUMENT_DECLINED", ex.Message);
    }

    [Fact]
    public async Task CardChallengeIsReportedAndStops()
    {
        var gateway = Gateway(
            request =>
            {
                if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
                {
                    return Json(HttpStatusCode.OK, """{"access_token":"TEST-TOKEN","expires_in":3600,"token_type":"Bearer"}""");
                }

                return Json(HttpStatusCode.UnprocessableEntity, """
                    {
                      "name": "UNPROCESSABLE_ENTITY",
                      "message": "The requested action could not be performed",
                      "details": [ { "issue": "PAYER_ACTION_REQUIRED", "description": "approve in browser" } ],
                      "debug_id": "dbg-1"
                    }
                    """);
            },
            out _);

        await Assert.ThrowsAsync<CardChallengeException>(() =>
            gateway.CreateOrderAsync(42, 10.00m, "USD", TestCard(), null, "req", CancellationToken.None));
    }
}