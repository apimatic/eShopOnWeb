using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public sealed class PaymentServiceTests
{
    [TestMethod]
    public async Task PreventsCrossShopperOrderAccess()
    {
        await using var db = Database();
        var service = Service(db, new FakePayPal());
        var order = await service.PlaceOrderAsync("shopper-a", OrderRequest(), default);

        var ex = await Assert.ThrowsExceptionAsync<PaymentOperationException>(() =>
            service.PayAsync("shopper-b", order.OrderId,
                new PayOrderRequest(TestCard(), null), default));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    public async Task RefundKeyReplaysAndDistinctRefundCannotExceedCapture()
    {
        await using var db = Database();
        var payPal = new FakePayPal();
        var service = Service(db, payPal);
        var order = await service.PlaceOrderAsync("shopper", OrderRequest(), default);
        await service.PayAsync("shopper", order.OrderId, new PayOrderRequest(TestCard(), null), default);
        await service.FulfilAsync(order.OrderId, default);

        var first = await service.RefundAsync("shopper", order.OrderId,
            new RefundOrderRequest(6m, "refund-one"), default);
        var replay = await service.RefundAsync("shopper", order.OrderId,
            new RefundOrderRequest(6m, "refund-one"), default);
        var ex = await Assert.ThrowsExceptionAsync<PaymentOperationException>(() =>
            service.RefundAsync("shopper", order.OrderId,
                new RefundOrderRequest(5m, "refund-two"), default));

        Assert.AreEqual(first.RefundId, replay.RefundId);
        Assert.AreEqual(1, payPal.RefundCalls);
        Assert.AreEqual(409, ex.StatusCode);
    }

    [TestMethod]
    public async Task SavedMethodCannotBeUsedByAnotherShopper()
    {
        await using var db = Database();
        var method = new SavedPaymentMethod("shopper-a", "create-key");
        method.Activate("token-a", "customer-a", "VISA", "1111", "2030-12", "CREDIT", "VERIFIED");
        db.SavedPaymentMethods.Add(method);
        await db.SaveChangesAsync();
        var service = Service(db, new FakePayPal());
        var order = await service.PlaceOrderAsync("shopper-b", OrderRequest(), default);

        var ex = await Assert.ThrowsExceptionAsync<PaymentOperationException>(() =>
            service.PayAsync("shopper-b", order.OrderId,
                new PayOrderRequest(null, method.Id), default));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    public async Task FreshLocalMethodRemainsVisibleWhileProviderListLags()
    {
        await using var db = Database();
        var method = new SavedPaymentMethod("shopper", "create-key");
        method.Activate("token", "customer", "VISA", "1111", "2030-12", "CREDIT", "VERIFIED");
        db.SavedPaymentMethods.Add(method);
        await db.SaveChangesAsync();
        var service = Service(db, new FakePayPal());

        var listed = await service.ListMethodsAsync("shopper", default);

        Assert.AreEqual(1, listed.Count);
        Assert.AreEqual(method.Id, listed[0].PaymentMethodId);
    }

    [TestMethod]
    public async Task FulfilmentRenewsAStaleAuthorizationBeforeCapture()
    {
        await using var db = Database();
        var payPal = new FakePayPal { AuthorizationCreatedAt = DateTimeOffset.UtcNow.AddDays(-4) };
        var service = Service(db, payPal);
        var order = await service.PlaceOrderAsync("shopper", OrderRequest(), default);
        await service.PayAsync("shopper", order.OrderId, new PayOrderRequest(TestCard(), null), default);

        var fulfilled = await service.FulfilAsync(order.OrderId, default);

        Assert.AreEqual(1, payPal.ReauthorizeCalls);
        Assert.AreEqual("Fulfilled", fulfilled.OrderStatus);
        Assert.AreEqual("COMPLETED", fulfilled.CaptureStatus);
    }

    private static CatalogContext Database()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var db = new CatalogContext(options);
        db.CatalogItems.Add(new CatalogItem(1, 1, "test", "test item", 10m, "test.png"));
        db.SaveChanges();
        return db;
    }

    private static PaymentService Service(CatalogContext db, IPayPalGateway payPal) => new(db, payPal,
        Options.Create(new PayPalOptions
        {
            Currency = "USD",
            Environment = "Sandbox"
        }));

    private static PlaceOrderRequest OrderRequest() => new(new[] { new OrderLineInput(1, 1) });

    private static CardInput TestCard() => new("Test Shopper", "4111111111111111", "2030-12", "123",
        new BillingAddressInput("1 Main St", null, "San Jose", "CA", "95131", "US"));

    private sealed class FakePayPal : IPayPalGateway
    {
        public int RefundCalls { get; private set; }
        public int ReauthorizeCalls { get; private set; }
        public DateTimeOffset AuthorizationCreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
            ProviderCard card, string createRequestId, string authorizeRequestId, CancellationToken ct) =>
            Task.FromResult(new AuthorizationResult("paypal-order", "COMPLETED", false,
                "authorization", "CREATED", amount, AuthorizationCreatedAt, AuthorizationCreatedAt.AddDays(3)));

        public Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
            Task.FromResult(new ProviderAuthorization(authorizationId, "CREATED", 10m,
                AuthorizationCreatedAt, AuthorizationCreatedAt.AddDays(3)));

        public Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken ct)
        {
            ReauthorizeCalls++;
            return Task.FromResult(new ProviderAuthorization(authorizationId, "CREATED", amount,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        }

        public Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken ct) =>
            Task.FromResult(new ProviderCapture("capture", "COMPLETED", amount, 0.59m, amount - 0.59m,
                DateTimeOffset.UtcNow));

        public Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct) =>
            Task.FromResult<string?>("VOIDED");

        public Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
            string requestId, CancellationToken ct)
        {
            RefundCalls++;
            return Task.FromResult(new ProviderRefund($"refund-{RefundCalls}", "COMPLETED", amount ?? 10m,
                DateTimeOffset.UtcNow));
        }

        public Task<ProviderSavedMethod> SaveMethodAsync(string ownerId, ProviderCard card, string requestId,
            CancellationToken ct) => Task.FromResult(new ProviderSavedMethod(
            "token", "customer", "VISA", "1111", "2030-12", "CREDIT", "VERIFIED"));

        public Task<IReadOnlyList<ProviderSavedMethod>> ListMethodsAsync(string customerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderSavedMethod>>(Array.Empty<ProviderSavedMethod>());

        public Task DeleteMethodAsync(string tokenId, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProviderTransaction>>(Array.Empty<ProviderTransaction>());
    }
}
