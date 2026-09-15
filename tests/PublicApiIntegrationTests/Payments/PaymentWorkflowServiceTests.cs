using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentWorkflowServiceTests
{
    private const string Buyer = "shopper@example.com";

    [TestMethod]
    public async Task PaymentOperationsAreIdempotentAndRefundsCannotExceedCapture()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 10.25m, "item.png"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);
        var order = await service.PlaceOrderAsync(Buyer, OrderRequest(2), CancellationToken.None);
        var payRequest = new PayOrderRequest(TestCard(), null);

        await service.PayAsync(Buyer, order.OrderId, payRequest, CancellationToken.None);
        await service.PayAsync(Buyer, order.OrderId, payRequest, CancellationToken.None);
        await service.FulfilAsync(order.OrderId, CancellationToken.None);
        await service.FulfilAsync(order.OrderId, CancellationToken.None);

        var first = await service.RefundAsync(Buyer, order.OrderId,
            new RefundOrderRequest(5m, "refund-one"), CancellationToken.None);
        var replay = await service.RefundAsync(Buyer, order.OrderId,
            new RefundOrderRequest(5m, "refund-one"), CancellationToken.None);
        await service.RefundAsync(Buyer, order.OrderId,
            new RefundOrderRequest(15.50m, "refund-two"), CancellationToken.None);

        Assert.AreEqual(1, payPal.AuthorizeCalls);
        Assert.AreEqual(1, payPal.CaptureCalls);
        Assert.AreEqual(2, payPal.RefundCalls);
        Assert.AreEqual(first.Refund.Id, replay.Refund.Id);
        Assert.AreEqual("Refunded", (await service.GetOrdersAsync(Buyer, CancellationToken.None)).Single().Payment.Status);

        var exception = await Assert.ThrowsExceptionAsync<PaymentApiException>(() => service.RefundAsync(Buyer,
            order.OrderId, new RefundOrderRequest(0.01m, "refund-three"), CancellationToken.None));
        Assert.AreEqual("ORDER_NOT_REFUNDABLE", exception.Code);
    }

    [TestMethod]
    public async Task SavedMethodsAndOrdersAreOwnerScopedAndDeletedMethodsDisappear()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "item", "Test item", 3m, "item.png"));
        await db.SaveChangesAsync();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);

        var method = await service.SavePaymentMethodAsync(Buyer,
            new SavePaymentMethodRequest(TestCard()), CancellationToken.None);
        Assert.AreEqual(1, (await service.GetPaymentMethodsAsync(Buyer, CancellationToken.None)).Count);
        Assert.AreEqual(0, (await service.GetPaymentMethodsAsync("other@example.com", CancellationToken.None)).Count);
        await Assert.ThrowsExceptionAsync<PaymentApiException>(() => service.DeletePaymentMethodAsync(
            "other@example.com", method.Id, CancellationToken.None));

        var order = await service.PlaceOrderAsync(Buyer, OrderRequest(1), CancellationToken.None);
        await Assert.ThrowsExceptionAsync<PaymentApiException>(() => service.PayAsync("other@example.com",
            order.OrderId, new PayOrderRequest(null, method.Id), CancellationToken.None));
        await service.PayAsync(Buyer, order.OrderId, new PayOrderRequest(null, method.Id), CancellationToken.None);

        await service.DeletePaymentMethodAsync(Buyer, method.Id, CancellationToken.None);
        Assert.AreEqual(0, (await service.GetPaymentMethodsAsync(Buyer, CancellationToken.None)).Count);
        Assert.AreEqual(1, payPal.DeleteTokenCalls);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new CatalogContext(options);
    }

    private static PaymentWorkflowService CreateService(CatalogContext db, IPayPalClient payPal) =>
        new(db, payPal, new OperationLock(), Options.Create(new PayPalOptions
        {
            ClientId = "test",
            ClientSecret = "test",
            Environment = "sandbox",
            Currency = "USD"
        }));

    private static PlaceOrderRequest OrderRequest(int quantity) => new(
        new[] { new OrderLineRequest(1, quantity) },
        new ShippingAddressRequest("1 Main St", "San Jose", "CA", "US", "95131"));

    private static CardDetails TestCard() => new("Test Buyer", "4111111111111111", "2030-12", "123",
        new BillingAddress("1 Main St", null, "San Jose", "CA", "95131", "US"));

    private sealed class FakePayPalClient : IPayPalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int DeleteTokenCalls { get; private set; }

        public Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentRequestId, decimal amount,
            string currency, CardDetails? card, string? vaultId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PayPalAuthorization($"ORDER-{orderId}", $"AUTH-{orderId}", "CREATED",
                amount, currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        }

        public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
            string currency, string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalAuthorization(string.Empty, authorizationId + "-R", "CREATED",
                amount, currency, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));

        public Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCapture("CAPTURE-1", "COMPLETED", amount, currency, 1m, amount - 1m));
        }

        public Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefund($"REFUND-{RefundCalls}", "COMPLETED", amount, currency));
        }

        public Task<PayPalPaymentToken> CreatePaymentTokenAsync(string buyerId, CardDetails card,
            string requestId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalPaymentToken("VAULT-1", "VISA", "1111", "2030-12"));

        public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
        {
            DeleteTokenCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
