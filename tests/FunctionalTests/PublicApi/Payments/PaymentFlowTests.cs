using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Xunit;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.Payments;

public class PaymentFlowTests
{
    [Fact]
    public async Task AuthorizeCaptureAndDistinctIdempotentRefundsAreSafe()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 10.25m, "item.png"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var service = new PaymentService(db, payPal);
        var order = await service.PlaceOrderAsync("shopper@example.com", OrderRequest(2), default);

        var authorized = await service.PayAsync("shopper@example.com", order.OrderId,
            new PayOrderRequest(Card(), null), default);
        var repeated = await service.PayAsync("shopper@example.com", order.OrderId,
            new PayOrderRequest(Card(), null), default);

        Assert.Equal("Authorized", authorized.Status);
        Assert.Equal(authorized.AuthorizationId, repeated.AuthorizationId);
        Assert.Equal(1, payPal.CreateOrderCalls);
        Assert.Equal(1, payPal.AuthorizeCalls);

        var captured = await service.FulfilAsync(order.OrderId, default);
        Assert.Equal(20.50m, captured.CapturedAmount);
        Assert.Equal(0.75m, captured.PayPalFee);
        Assert.Equal(19.75m, captured.NetAmount);

        var first = await service.RefundAsync("shopper@example.com", order.OrderId,
            new RefundOrderRequest(5m, "return-line-1", null), default);
        var repeatedFirst = await service.RefundAsync("shopper@example.com", order.OrderId,
            new RefundOrderRequest(5m, "return-line-1", null), default);
        var second = await service.RefundAsync("shopper@example.com", order.OrderId,
            new RefundOrderRequest(5m, "return-line-2", null), default);

        Assert.Equal(first.RefundId, repeatedFirst.RefundId);
        Assert.NotEqual(first.RefundId, second.RefundId);
        Assert.Equal(2, payPal.RefundCalls);
        await Assert.ThrowsAsync<PaymentApiException>(() => service.RefundAsync("shopper@example.com",
            order.OrderId, new RefundOrderRequest(11m, "too-much", null), default));
    }

    [Fact]
    public async Task SavedCardIsShopperScopedAndCannotBeUsedAfterDeletion()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 3m, "item.png"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var methods = new PaymentMethodService(db, payPal);
        var payments = new PaymentService(db, payPal);

        var saved = await methods.CreateAsync("owner@example.com", new SavePaymentMethodRequest(Card()), default);
        Assert.Single(await methods.ListAsync("owner@example.com", default));
        Assert.Empty(await methods.ListAsync("other@example.com", default));

        var otherOrder = await payments.PlaceOrderAsync("other@example.com", OrderRequest(1), default);
        var ownershipError = await Assert.ThrowsAsync<PaymentApiException>(() => payments.PayAsync(
            "other@example.com", otherOrder.OrderId, new PayOrderRequest(null, saved.PaymentMethodId), default));
        Assert.Equal(404, ownershipError.StatusCode);

        await methods.DeleteAsync("owner@example.com", saved.PaymentMethodId, default);
        Assert.Empty(await methods.ListAsync("owner@example.com", default));
        var ownerOrder = await payments.PlaceOrderAsync("owner@example.com", OrderRequest(1), default);
        await Assert.ThrowsAsync<PaymentApiException>(() => payments.PayAsync("owner@example.com",
            ownerOrder.OrderId, new PayOrderRequest(null, saved.PaymentMethodId), default));
    }

    [Fact]
    public async Task ShopperCannotActOnAnotherShoppersOrder()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 3m, "item.png"));
        await db.SaveChangesAsync();
        var service = new PaymentService(db, new FakePayPalClient());
        var order = await service.PlaceOrderAsync("owner@example.com", OrderRequest(1), default);
        var error = await Assert.ThrowsAsync<PaymentApiException>(() => service.PayAsync("other@example.com",
            order.OrderId, new PayOrderRequest(Card(), null), default));
        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task FreshLocalCaptureIsPendingReportingRatherThanAReconciliationGap()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 20.50m, "item.png"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var payments = new PaymentService(db, payPal);
        var order = await payments.PlaceOrderAsync("shopper@example.com", OrderRequest(1), default);
        await payments.PayAsync("shopper@example.com", order.OrderId, new PayOrderRequest(Card(), null), default);
        await payments.FulfilAsync(order.OrderId, default);

        var report = await new ReconciliationService(db, payPal).BuildAsync(
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1), default);

        Assert.Equal(0, report.MissingInPayPal);
        Assert.Equal(1, report.PendingPayPalReporting);
        Assert.Equal("PendingPayPalReporting", Assert.Single(report.Entries).MatchStatus);
    }

    private static CatalogContext CreateContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PlaceOrderRequest OrderRequest(int quantity) => new(
        new[] { new PlaceOrderItemRequest(1, quantity) },
        new ShippingAddressRequest("1 Main St", "San Jose", "CA", "US", "95131"));

    private static CardInput Card() => new("Sandbox Shopper", "4111111111111111", "2030-12", "123",
        new BillingAddressInput("1 Main St", null, "San Jose", "CA", "95131", "US"));
}

internal sealed class FakePayPalClient : IPayPalClient
{
    private int _refundSequence;
    public string Currency => "USD";
    public int CreateOrderCalls { get; private set; }
    public int AuthorizeCalls { get; private set; }
    public int RefundCalls { get; private set; }

    public Task<PayPalOrderResult> CreateOrderAsync(int orderId, string invoiceId, decimal amount, CancellationToken cancellationToken)
    {
        CreateOrderCalls++;
        return Task.FromResult(new PayPalOrderResult($"ORDER-{orderId}", "CREATED"));
    }

    public Task<PayPalAuthorizationResult> AuthorizeAsync(string payPalOrderId, CardInput? card, string? vaultId,
        string invoiceId, CancellationToken cancellationToken)
    {
        AuthorizeCalls++;
        return Task.FromResult(new PayPalAuthorizationResult($"AUTH-{invoiceId}", "CREATED", 20.50m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
    }

    public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        Task.FromResult(new PayPalAuthorizationResult(authorizationId, "CREATED", 20.50m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));

    public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string invoiceId,
        CancellationToken cancellationToken) => Task.FromResult(new PayPalAuthorizationResult(authorizationId,
        "CREATED", amount, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));

    public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        CancellationToken cancellationToken) => Task.FromResult(new PayPalCaptureResult($"CAP-{invoiceId}",
        "COMPLETED", amount, .75m, amount - .75m, DateTimeOffset.UtcNow));

    public Task VoidAsync(string authorizationId, string invoiceId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string invoiceId, string idempotencyKey,
        string? note, CancellationToken cancellationToken)
    {
        RefundCalls++;
        return Task.FromResult(new PayPalRefundResult($"REF-{++_refundSequence}", "COMPLETED", amount,
            DateTimeOffset.UtcNow));
    }

    public Task<PayPalVaultResult> CreatePaymentTokenAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken) => Task.FromResult(new PayPalVaultResult("VAULT-1", "VISA", "1111", "2030-12"));

    public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<PayPalTransaction>> SearchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
}
