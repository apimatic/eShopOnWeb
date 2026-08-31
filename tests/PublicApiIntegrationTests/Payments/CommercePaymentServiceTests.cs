using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class CommercePaymentServiceTests
{
    [TestMethod]
    public async Task MoneyMovementIsIdempotentAndPartialRefundsCannotExceedCapture()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Item", 12.34m, "picture"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);

        var order = await service.PlaceOrderAsync("buyer@example.com", OrderRequest(quantity: 2), default);
        Assert.AreEqual(24.68m, order.Total());

        var card = new PayOrderRequest { Card = Card() };
        await service.PayAsync("buyer@example.com", order.Id, card, default);
        await service.PayAsync("buyer@example.com", order.Id, card, default);
        Assert.AreEqual(1, payPal.AuthorizeCalls);

        var fulfilled = await service.FulfilAsync(order.Id, default);
        await service.FulfilAsync(order.Id, default);
        Assert.AreEqual(OrderStatus.Fulfilled, fulfilled.Status);
        Assert.AreEqual(1, payPal.CaptureCalls);
        Assert.AreEqual(0.74m, fulfilled.Payment!.PayPalFee);
        Assert.AreEqual(23.94m, fulfilled.Payment.NetAmount);

        var first = await service.RefundAsync("buyer@example.com", order.Id,
            new RefundOrderRequest { IdempotencyKey = "first", Amount = 5m }, default);
        var repeated = await service.RefundAsync("buyer@example.com", order.Id,
            new RefundOrderRequest { IdempotencyKey = "first", Amount = 5m }, default);
        var second = await service.RefundAsync("buyer@example.com", order.Id,
            new RefundOrderRequest { IdempotencyKey = "second", Amount = 4m }, default);

        Assert.AreEqual(first.PayPalRefundId, repeated.PayPalRefundId);
        Assert.AreNotEqual(first.PayPalRefundId, second.PayPalRefundId);
        Assert.AreEqual(2, payPal.RefundCalls);
        Assert.AreEqual(9m, order.Payment!.RefundedAmount);
        await Assert.ThrowsExceptionAsync<PaymentStateException>(() => service.RefundAsync(
            "buyer@example.com", order.Id,
            new RefundOrderRequest { IdempotencyKey = "too-much", Amount = 20m }, default));
    }

    [TestMethod]
    public async Task SavedCardCannotBeUsedOrDeletedByAnotherShopper()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Item", 10m, "picture"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);
        var method = await service.SavePaymentMethodAsync("owner@example.com", Card(), default);
        var strangersOrder = await service.PlaceOrderAsync("stranger@example.com", OrderRequest(), default);

        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => service.PayAsync(
            "stranger@example.com", strangersOrder.Id,
            new PayOrderRequest { PaymentMethodId = method.Id }, default));
        await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => service.DeletePaymentMethodAsync(
            "stranger@example.com", method.Id, default));

        Assert.AreEqual(0, payPal.AuthorizeCalls);
        Assert.AreEqual(0, payPal.DeleteCalls);
    }

    [TestMethod]
    public async Task StaleAuthorizationIsRenewedBeforeCapture()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Item", 12.34m, "picture"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient { AuthorizationCreatedAt = DateTimeOffset.UtcNow.AddDays(-4) };
        var service = CreateService(db, payPal);
        var order = await service.PlaceOrderAsync("buyer@example.com", OrderRequest(quantity: 2), default);
        await service.PayAsync("buyer@example.com", order.Id, new PayOrderRequest { Card = Card() }, default);

        await service.FulfilAsync(order.Id, default);

        Assert.AreEqual(1, payPal.ReauthorizeCalls);
        Assert.AreEqual(1, payPal.CaptureCalls);
    }

    [TestMethod]
    public async Task ReconciliationReadsEveryPayPalPageAndShowsBothKindsOfMismatch()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient
        {
            TransactionPages = new Dictionary<int, PayPalTransactionPage>
            {
                [1] = new(new[] { Transaction("paypal-only") }, 1, 2, DateTimeOffset.UtcNow),
                [2] = new(new[] { Transaction("another-paypal-only") }, 2, 2, DateTimeOffset.UtcNow)
            }
        };
        var service = CreateService(db, payPal);
        var to = DateTimeOffset.UtcNow;
        var report = await service.ReconcileAsync(to.AddDays(-1), to, default);

        Assert.AreEqual(2, payPal.SearchCalls);
        Assert.AreEqual(2, report.Entries.Count(e => e.MatchStatus == "PayPalOnly"));
    }

    private static CatalogContext CreateContext()
        => new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CommercePaymentService CreateService(CatalogContext db, IPayPalClient payPal)
        => new(db, payPal, Options.Create(new PayPalOptions
        {
            ClientId = "test",
            ClientSecret = "test",
            Environment = "sandbox",
            Currency = "USD"
        }));

    private static PlaceOrderRequest OrderRequest(int quantity = 1) => new()
    {
        Items = new() { new PlaceOrderItemRequest { CatalogItemId = 1, Quantity = quantity } },
        ShippingAddress = new ShippingAddressRequest
        {
            Street = "1 Main St", City = "San Jose", State = "CA", Country = "US", ZipCode = "95131"
        }
    };

    private static CardRequest Card() => new()
    {
        Name = "Test Buyer",
        Number = "4111111111111111",
        Expiry = "2030-12",
        SecurityCode = "123",
        BillingAddress = new BillingAddressRequest
        {
            AddressLine1 = "1 Main St", City = "San Jose", State = "CA", PostalCode = "95131", CountryCode = "US"
        }
    };

    private static PayPalTransaction Transaction(string id) => new(
        id, null, "S", "T0006", 1m, "USD", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakePayPalClient : IPayPalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int ReauthorizeCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public Dictionary<int, PayPalTransactionPage> TransactionPages { get; init; } = new();
        public DateTimeOffset AuthorizationCreatedAt { get; init; } = DateTimeOffset.UtcNow;

        public Task<PayPalAuthorization> AuthorizeAsync(string externalReference, int authorizationAttempt, decimal amount, string currency, PayPalCard? card, string? vaultId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PayPalAuthorization(
                "ORDER-1", "COMPLETED", "AUTH-1", "CREATED", amount, currency,
                AuthorizationCreatedAt, AuthorizationCreatedAt.AddDays(29), "VISA", "1111"));
        }

        public Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
            => Task.FromResult(new PayPalAuthorizationDetails(
                authorizationId, "CREATED", 24.68m, "USD", AuthorizationCreatedAt, AuthorizationCreatedAt.AddDays(29)));

        public Task<PayPalAuthorizationDetails> ReauthorizeAsync(string externalReference, string authorizationId, decimal amount, string currency, CancellationToken cancellationToken)
        {
            ReauthorizeCalls++;
            return Task.FromResult(new PayPalAuthorizationDetails(authorizationId, "CREATED", amount, currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        }

        public Task<PayPalCapture> CaptureAsync(string externalReference, string authorizationId, decimal amount, string currency, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCapture("CAPTURE-1", "COMPLETED", amount, currency, .74m, amount - .74m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
            => Task.FromResult(new PayPalCapture(captureId, "COMPLETED", 24.68m, "USD", .74m, 23.94m, DateTimeOffset.UtcNow));

        public Task VoidAsync(string externalReference, string authorizationId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PayPalRefund> RefundAsync(string requestId, string captureId, decimal amount, string currency, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefund($"REFUND-{RefundCalls}", "COMPLETED", amount, currency, DateTimeOffset.UtcNow));
        }

        public Task<PayPalSavedCard> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken)
            => Task.FromResult(new PayPalSavedCard("VAULT-1", "CUSTOMER-1", "VISA", "1111", card.Expiry));

        public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<PayPalTransactionPage> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(TransactionPages.TryGetValue(page, out var result)
                ? result
                : new PayPalTransactionPage(Array.Empty<PayPalTransaction>(), page, 0, null));
        }
    }
}
