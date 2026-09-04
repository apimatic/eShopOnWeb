using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Gateway behaviour against the SDK's real wire mapping, with the HttpClient seam stubbed:
/// request shapes on the wire, fee/net reads, paging, and the error/idempotency boundaries.
/// </summary>
public class PayPalPaymentGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            return _responder(request);
        }
    }

    private const string TokenJson = """{"access_token":"TEST-TOKEN","token_type":"Bearer","expires_in":3600}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (PayPalPaymentGateway Gateway, StubHandler Handler) GatewayWith(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var httpClient = new HttpClient(new SingleSendGuardHandler { InnerHandler = handler });
        var options = new PayPalServerSdkClientOptions
        {
            Environment = ServerEnvironment.Sandbox,
            Oauth2 = new OAuth2ClientCredentials { ClientId = "test-client", ClientSecret = "test-secret" }
        };
        var client = new PayPalServerSdkClient(httpClient, options);
        return (new PayPalPaymentGateway(client, NullLogger<PayPalPaymentGateway>.Instance), handler);
    }

    private static HttpResponseMessage Route(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var json = path switch
        {
            var p when p.EndsWith("/v1/oauth2/token") => TokenJson,
            var p when p.EndsWith("/v2/checkout/orders") => """{"id":"PP-ORDER-1","status":"CREATED"}""",
            var p when p.Contains("/authorize") => """{"id":"PP-ORDER-1","status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[{"id":"AUTH-1","status":"CREATED","amount":{"currency_code":"USD","value":"9.60"},"expiration_time":"2026-09-07T10:00:00Z","network_transaction_reference":{"id":"NTR-9"}}]}}]}""",
            _ => "{}"
        };
        return Json(HttpStatusCode.Created, json);
    }

    [Fact]
    public async Task Authorize_sends_intent_authorize_with_exact_amount_and_returns_the_hold()
    {
        var (gateway, handler) = GatewayWith(Route);

        var result = await gateway.AuthorizeAsync(new GatewayAuthorizeRequest(
            9.6m, "USD", "eshop-order-7-a1b2c3d4e5", "eshop-order-7",
            new GatewayAuthorizeSource(new CardCredential("4111111111111111", "09/2029", "123", "Test Buyer", null), null, null)));

        Assert.Equal("AUTH-1", result.AuthorizationId);
        Assert.Equal("PP-ORDER-1", result.ProviderOrderId);
        Assert.Equal(9.6m, result.Amount);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.Zero), result.ExpirationTime!.Value.ToUniversalTime());
        Assert.Equal("NTR-9", result.NetworkTransactionReference);

        var createBody = handler.Bodies.Single(b => b.Contains("\"intent\""));
        Assert.Contains("\"intent\":\"AUTHORIZE\"", createBody);
        Assert.Contains("\"value\":\"9.60\"", createBody);
        Assert.Contains("\"currency_code\":\"USD\"", createBody);
        Assert.Contains("\"invoice_id\":\"eshop-order-7-a1b2c3d4e5\"", createBody);
        Assert.Contains("\"custom_id\":\"eshop-order-7\"", createBody);

        var authorizeBody = handler.Bodies.Single(b => b.Contains("payment_source") && b.Contains("card"));
        Assert.Contains("\"number\":\"4111111111111111\"", authorizeBody);
        Assert.Contains("\"expiry\":\"2029-09\"", authorizeBody); // PayPal wire format is YYYY-MM

        var last = handler.Requests.Last();
        Assert.StartsWith("https://api-m.sandbox.paypal.com", last.RequestUri!.ToString());
        Assert.Equal("Bearer TEST-TOKEN", last.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Authorize_maps_a_provider_decline_to_a_caller_safe_rejection()
    {
        var (gateway, _) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            if (path.EndsWith("/v2/checkout/orders")) return Json(HttpStatusCode.Created, """{"id":"PP-ORDER-1","status":"CREATED"}""");
            return Json(HttpStatusCode.UnprocessableEntity,
                """{"name":"UNPROCESSABLE_ENTITY","message":"The requested action could not be performed.","debug_id":"dbg-1","details":[{"field":"/payment_source/card","issue":"NOT_SUPPORTED_BY_ACQUIRER","description":"Card type not supported by the acquirer."}]}""");
        });

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(
            new GatewayAuthorizeRequest(10m, "USD", "eshop-order-8-a1b2c3d4e5", "eshop-order-8",
                new GatewayAuthorizeSource(new CardCredential("4111111111111111", "09/2029", "123", null, null), null, null))));

        Assert.Equal(PaymentFailureKind.ProviderRejected, ex.Kind);
        Assert.Equal("UNPROCESSABLE_ENTITY", ex.ProviderErrorName);
        Assert.Equal("NOT_SUPPORTED_BY_ACQUIRER", ex.ProviderIssue);
        Assert.Equal("PP-ORDER-1", ex.ProviderOrderId);
        Assert.DoesNotContain("4111111111111111", ex.Message);
    }

    [Fact]
    public async Task A_write_is_never_re_sent_after_a_transport_failure()
    {
        var (gateway, handler) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            throw new HttpRequestException("connection reset");
        });

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.AuthorizeAsync(
            new GatewayAuthorizeRequest(10m, "USD", "eshop-order-9-a1b2c3d4e5", "eshop-order-9",
                new GatewayAuthorizeSource(new CardCredential("4111111111111111", "09/2029", "123", null, null), null, null))));

        Assert.Equal(PaymentFailureKind.OutcomeUnknown, ex.Kind);
        Assert.Equal(1, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/v2/checkout/orders")));
    }

    [Fact]
    public async Task Capture_reads_gross_fee_and_net_from_the_seller_receivable_breakdown()
    {
        var (gateway, handler) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            return Json(HttpStatusCode.Created,
                """{"id":"CAP-1","status":"COMPLETED","amount":{"currency_code":"USD","value":"9.60"},"seller_receivable_breakdown":{"gross_amount":{"currency_code":"USD","value":"9.60"},"paypal_fee":{"currency_code":"USD","value":"0.56"},"net_amount":{"currency_code":"USD","value":"9.04"}},"supplementary_data":{"related_ids":{"order_id":"PP-ORDER-1","authorization_id":"AUTH-1"}}}""");
        });

        var capture = await gateway.CaptureAsync("AUTH-1", 9.6m, "USD");

        Assert.Equal("CAP-1", capture.CaptureId);
        Assert.Equal("COMPLETED", capture.Status);
        Assert.Equal(9.6m, capture.GrossAmount);
        Assert.Equal(0.56m, capture.FeeAmount);
        Assert.Equal(9.04m, capture.NetAmount);
        Assert.Equal("AUTH-1", capture.AuthorizationId);
        Assert.Contains("\"final_capture\":true", handler.Bodies.Last());
        Assert.Contains("/v2/payments/authorizations/AUTH-1/capture", handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task VaultCard_returns_token_and_display_fields_and_keeps_no_pan_on_the_response()
    {
        var (gateway, handler) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            return Json(HttpStatusCode.Created,
                """{"id":"C-TOKEN-1","customer":{"id":"CUST-1","merchant_customer_id":"demouser@microsoft.com"},"payment_source":{"card":{"last_digits":"1111","brand":"VISA","expiry":"09/2029","name":"Test Buyer"}}}""");
        });

        var saved = await gateway.VaultCardAsync("demouser@microsoft.com", new CardCredential("4111111111111111", "09/2029", "123", "Test Buyer", null));

        Assert.Equal("C-TOKEN-1", saved.TokenId);
        Assert.Equal("CUST-1", saved.VaultCustomerId);
        Assert.Equal("VISA", saved.Brand);
        Assert.Equal("1111", saved.Last4);
        Assert.DoesNotContain("4111111111111111", System.Text.Json.JsonSerializer.Serialize(saved));
        Assert.Contains("/v3/vault/payment-tokens", handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SearchTransactions_pages_through_the_whole_range()
    {
        var (gateway, handler) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);

            var query = request.RequestUri!.Query;
            var page = query.Contains("page=2") ? "2" : "1";
            var items = page switch
            {
                "1" => """{"transaction_details":[{"transaction_info":{"transaction_id":"T-1","transaction_status":"S","transaction_amount":{"currency_code":"USD","value":"10.00"},"fee_amount":{"currency_code":"USD","value":"-0.59"}}},{"transaction_info":{"transaction_id":"T-2","transaction_status":"S","transaction_amount":{"currency_code":"USD","value":"20.00"},"fee_amount":{"currency_code":"USD","value":"-1.02"}}}],"page":1,"total_items":4,"total_pages":2}""",
                _ => """{"transaction_details":[{"transaction_info":{"transaction_id":"T-3","transaction_status":"S","transaction_amount":{"currency_code":"USD","value":"30.00"},"fee_amount":{"currency_code":"USD","value":"-1.45"}}},{"transaction_info":{"transaction_id":"T-4","transaction_status":"S","transaction_amount":{"currency_code":"USD","value":"40.00"},"fee_amount":{"currency_code":"USD","value":"-1.88"}}}],"page":2,"total_items":4,"total_pages":2}""",
            };
            return Json(HttpStatusCode.OK, items);
        });

        var results = await gateway.SearchTransactionsAsync(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero));

        var searches = handler.Requests.Where(r => r.RequestUri!.AbsolutePath.Contains("/reporting/transactions")).ToList();
        Assert.True(searches.Count >= 2, $"expected paging to reach a second page; queries: {string.Join(" | ", searches.Select(s => s.RequestUri!.Query))}");
        Assert.Equal(4, results.Count);
        Assert.Equal(new[] { "T-1", "T-2", "T-3", "T-4" }, results.Select(r => r.TransactionId));
        Assert.Equal(10m, results[0].Amount);
        Assert.Equal(9.41m, results[0].NetAmount);
        var query = searches[0].RequestUri!.Query;
        Assert.Contains("start_date=2026-08-01T00%3A00%3A00Z", query);
        Assert.Contains("end_date=2026-08-20T00%3A00%3A00Z", query);
    }

    [Fact]
    public async Task SearchTransactions_splits_ranges_beyond_the_provider_window()
    {
        var (gateway, handler) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            return Json(HttpStatusCode.OK, """{"transaction_details":[],"page":1,"total_items":0,"total_pages":0}""");
        });

        var results = await gateway.SearchTransactionsAsync(
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(results);
        var searches = handler.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("/reporting/transactions"));
        Assert.True(searches >= 3, $"a 92-day range must be covered by at least 3 ≤31-day windows, got {searches}");
    }

    [Fact]
    public async Task Unknown_resources_surface_as_not_found()
    {
        var (gateway, _) = GatewayWith(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/oauth2/token")) return Json(HttpStatusCode.OK, TokenJson);
            return Json(HttpStatusCode.NotFound, """{"name":"RESOURCE_NOT_FOUND","message":"Resource not found.","debug_id":"dbg-2"}""");
        });

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => gateway.GetAuthorizationAsync("AUTH-MISSING"));

        Assert.Equal(PaymentFailureKind.ResourceNotFound, ex.Kind);
    }
}
