using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentEndpointsTest
{
    [TestMethod]
    public async Task FullPaymentLifecycleIsIdempotentAndReturnsTopLevelIdentifiers()
    {
        var gateway = new FakePayPalGateway();
        await using var app = Factory(gateway);
        var shopper = app.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var create = await shopper.PostAsJsonAsync("/api/orders", NewOrder());
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.IsNotNull(created);
        Assert.IsTrue(created.OrderId > 0);
        Assert.AreEqual("AwaitingPayment", created.PaymentState);
        Assert.AreEqual(19.50m, created.Total);

        var payBody = new PayOrderRequest(TestCard(), null);
        var firstPay = await shopper.PostAsJsonAsync($"/api/orders/{created.OrderId}/pay", payBody);
        firstPay.EnsureSuccessStatusCode();
        var secondPay = await shopper.PostAsJsonAsync($"/api/orders/{created.OrderId}/pay", payBody);
        secondPay.EnsureSuccessStatusCode();
        Assert.AreEqual(1, gateway.AuthorizeCalls, "A repeated pay request must not authorize twice.");

        var firstFulfil = await admin.PostAsync($"/api/orders/{created.OrderId}/fulfil", null);
        firstFulfil.EnsureSuccessStatusCode();
        var secondFulfil = await admin.PostAsync($"/api/orders/{created.OrderId}/fulfil", null);
        secondFulfil.EnsureSuccessStatusCode();
        Assert.AreEqual(1, gateway.CaptureCalls, "A repeated fulfil request must not capture twice.");

        var refundBody = new RefundOrderRequest("refund-lifecycle-1", 5.25m);
        var firstRefund = await shopper.PostAsJsonAsync($"/api/orders/{created.OrderId}/refunds", refundBody);
        Assert.AreEqual(HttpStatusCode.Created, firstRefund.StatusCode);
        var refund = await firstRefund.Content.ReadFromJsonAsync<RefundOrderResponse>();
        Assert.IsNotNull(refund);
        Assert.IsFalse(string.IsNullOrWhiteSpace(refund.RefundId));
        var secondRefund = await shopper.PostAsJsonAsync($"/api/orders/{created.OrderId}/refunds", refundBody);
        Assert.AreEqual(HttpStatusCode.Created, secondRefund.StatusCode);
        Assert.AreEqual(1, gateway.RefundCalls, "A repeated refund key must not refund twice.");

        var myOrders = await shopper.GetFromJsonAsync<List<MyOrderResponse>>("/api/my-orders");
        Assert.IsNotNull(myOrders);
        Assert.IsTrue(myOrders.Any(x => x.OrderId == created.OrderId && x.PaymentState == "PartiallyRefunded"));
    }

    [TestMethod]
    public async Task ShopperCannotUseOperatorRoutesAndSavedCardCanBeRemoved()
    {
        var gateway = new FakePayPalGateway();
        await using var app = Factory(gateway);
        var shopper = app.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var fulfil = await shopper.PostAsync("/api/orders/1/fulfil", null);
        var cancel = await shopper.PostAsync("/api/orders/1/cancel", null);
        var reconciliation = await shopper.GetAsync("/api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconciliation.StatusCode);

        var savedResponse = await shopper.PostAsJsonAsync("/api/payment-methods", new SavePaymentMethodRequest(TestCard()));
        Assert.AreEqual(HttpStatusCode.Created, savedResponse.StatusCode);
        var saved = await savedResponse.Content.ReadFromJsonAsync<PaymentMethodResponse>();
        Assert.IsNotNull(saved);
        Assert.IsFalse(string.IsNullOrWhiteSpace(saved.PaymentMethodId));
        Assert.AreEqual("1111", saved.LastDigits);

        var methods = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("/api/payment-methods");
        Assert.AreEqual(1, methods?.Count);
        var deleted = await shopper.DeleteAsync($"/api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        methods = await shopper.GetFromJsonAsync<List<PaymentMethodResponse>>("/api/payment-methods");
        Assert.AreEqual(0, methods?.Count);
    }

    [TestMethod]
    public async Task EndpointsRequireAJwt()
    {
        var gateway = new FakePayPalGateway();
        await using var app = Factory(gateway);
        var client = app.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/payment-methods")).StatusCode);
    }

    [TestMethod]
    public async Task FulfilRenewsAnAuthorizationOutsideTheThreeDayHonorPeriod()
    {
        var gateway = new FakePayPalGateway { ReturnStaleAuthorization = true };
        await using var app = Factory(gateway);
        var shopper = app.CreateClient();
        shopper.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        var created = await (await shopper.PostAsJsonAsync("/api/orders", NewOrder()))
            .Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.IsNotNull(created);
        (await shopper.PostAsJsonAsync($"/api/orders/{created.OrderId}/pay", new PayOrderRequest(TestCard(), null)))
            .EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/orders/{created.OrderId}/fulfil", null)).EnsureSuccessStatusCode();
        Assert.AreEqual(1, gateway.ReauthorizeCalls);
        Assert.AreEqual(1, gateway.CaptureCalls);
    }

    private static WebApplicationFactory<Program> Factory(FakePayPalGateway gateway) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPayPalGateway>();
            services.AddSingleton<IPayPalGateway>(gateway);
        }));

    private static PlaceOrderRequest NewOrder() => new(
        new[] { new PlaceOrderItemRequest(1, 1) },
        new ShippingAddressRequest("1 Test Street", "Seattle", "WA", "US", "98101"));

    private static CardRequestDto TestCard() => new("Test Shopper", "test-card-number", "2030-12", "123",
        new CardBillingAddressDto("1 Test Street", null, "Seattle", "WA", "98101", "US"));

    private sealed class FakePayPalGateway : IPayPalGateway
    {
        private readonly HashSet<string> _tokens = new(StringComparer.Ordinal);
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int ReauthorizeCalls { get; private set; }
        public bool ReturnStaleAuthorization { get; init; }

        public Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
            string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
            string? existingPayPalOrderId, CancellationToken ct)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PayPalAuthorizationResult($"PAYPAL-ORDER-{orderId}", "COMPLETED",
                $"AUTH-{orderId}", "CREATED", amount, currency, DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow));
        }

        public Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
            Task.FromResult(new PayPalAuthorizationSnapshot(authorizationId, "CREATED", 19.50m, "USD",
                DateTimeOffset.UtcNow.AddDays(20),
                ReturnStaleAuthorization ? DateTimeOffset.UtcNow.AddDays(-4) : DateTimeOffset.UtcNow));

        public Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken ct)
        {
            ReauthorizeCalls++;
            return Task.FromResult(new PayPalAuthorizationSnapshot(authorizationId, "CREATED", amount, currency,
                DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow));
        }

        public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
            string currency, string requestId, CancellationToken ct)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCaptureResult($"CAPTURE-{orderId}", "COMPLETED", amount,
                currency, amount, 0.75m, amount - 0.75m));
        }

        public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct) =>
            Task.FromResult(new PayPalCaptureResult(captureId, "COMPLETED", 19.50m, "USD", 19.50m, 0.75m, 18.75m));

        public Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct) =>
            Task.FromResult<string?>("VOIDED");

        public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
            bool refundRemainingBalance, string idempotencyKey, int orderId, CancellationToken ct)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefundResult($"REFUND-{idempotencyKey}", "COMPLETED", amount, currency));
        }

        public Task<PayPalSavedCardResult> SaveCardAsync(string ownerCorrelation, string? customerId,
            CardInput card, string setupRequestId, string tokenRequestId, CancellationToken ct)
        {
            const string token = "TOKEN-1";
            _tokens.Add(token);
            return Task.FromResult(new PayPalSavedCardResult(token, customerId ?? "CUSTOMER-1", "VISA", "1111", "2030-12", "Test Shopper"));
        }

        public Task<IReadOnlySet<string>> ListPaymentTokenIdsAsync(string customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<string>>(_tokens);

        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct)
        {
            _tokens.Remove(tokenId);
            return Task.CompletedTask;
        }

        public Task<PayPalTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken ct) => Task.FromResult(new PayPalTransactionReport(Array.Empty<PayPalTransactionRecord>(), null, 1));
    }
}
