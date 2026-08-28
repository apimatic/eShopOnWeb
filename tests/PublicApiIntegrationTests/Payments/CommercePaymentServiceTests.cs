using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class CommercePaymentServiceTests
{
    [TestMethod]
    public async Task PaymentCaptureAndRefundsAreIdempotentAndBounded()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);
        var order = await CreateOrderAsync(service, db, "shopper@example.com");

        var paid = await service.PayAsync(order.OrderId, "shopper@example.com", new PayOrderRequest
        {
            Card = TestCard()
        }, CancellationToken.None);
        var paidAgain = await service.PayAsync(order.OrderId, "shopper@example.com", new PayOrderRequest
        {
            Card = TestCard()
        }, CancellationToken.None);

        Assert.AreEqual("Authorized", paid.PaymentStatus);
        Assert.AreEqual(paid.AuthorizationId, paidAgain.AuthorizationId);
        Assert.AreEqual(1, payPal.AuthorizeCalls);

        var fulfilled = await service.FulfilAsync(order.OrderId, CancellationToken.None);
        var fulfilledAgain = await service.FulfilAsync(order.OrderId, CancellationToken.None);
        Assert.AreEqual("Fulfilled", fulfilled.FulfillmentStatus);
        Assert.AreEqual(0.50m, fulfilled.PayPalFee);
        Assert.AreEqual(9.50m, fulfilled.NetAmount);
        Assert.AreEqual(fulfilled.CaptureId, fulfilledAgain.CaptureId);
        Assert.AreEqual(1, payPal.CaptureCalls);

        var first = await service.RefundAsync(order.OrderId, "shopper@example.com", new RefundOrderRequest
        {
            IdempotencyKey = "return-line-1",
            Amount = 3m
        }, CancellationToken.None);
        var replay = await service.RefundAsync(order.OrderId, "shopper@example.com", new RefundOrderRequest
        {
            IdempotencyKey = "return-line-1",
            Amount = 3m
        }, CancellationToken.None);
        var second = await service.RefundAsync(order.OrderId, "shopper@example.com", new RefundOrderRequest
        {
            IdempotencyKey = "return-line-2",
            Amount = 4m
        }, CancellationToken.None);

        Assert.AreEqual(first.RefundId, replay.RefundId);
        Assert.AreNotEqual(first.RefundId, second.RefundId);
        Assert.AreEqual(2, payPal.RefundCalls);
        Assert.AreEqual(3m, second.RemainingRefundableAmount);

        var overRefund = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.RefundAsync(order.OrderId, "shopper@example.com", new RefundOrderRequest
            {
                IdempotencyKey = "return-too-much",
                Amount = 3.01m
            }, CancellationToken.None));
        Assert.AreEqual(409, overRefund.StatusCode);
        Assert.AreEqual(2, payPal.RefundCalls);
    }

    [TestMethod]
    public async Task ShopperCannotSeeUseOrDeleteAnotherShoppersCardOrOrder()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);
        var saved = await service.SavePaymentMethodAsync("owner@example.com", new SavePaymentMethodRequest
        {
            Card = TestCard()
        }, CancellationToken.None);
        var order = await CreateOrderAsync(service, db, "owner@example.com");

        var otherCards = await service.GetPaymentMethodsAsync("other@example.com", CancellationToken.None);
        Assert.AreEqual(0, otherCards.Count);

        var deleteOther = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.DeletePaymentMethodAsync(saved.PaymentMethodId, "other@example.com", CancellationToken.None));
        Assert.AreEqual(404, deleteOther.StatusCode);

        var payOtherOrder = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.PayAsync(order.OrderId, "other@example.com", new PayOrderRequest
            {
                PaymentMethodId = saved.PaymentMethodId
            }, CancellationToken.None));
        Assert.AreEqual(404, payOtherOrder.StatusCode);

        await service.DeletePaymentMethodAsync(saved.PaymentMethodId, "owner@example.com", CancellationToken.None);
        Assert.AreEqual(1, payPal.DeleteTokenCalls);
        Assert.AreEqual(0, (await service.GetPaymentMethodsAsync("owner@example.com", CancellationToken.None)).Count);

        var useDeleted = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.PayAsync(order.OrderId, "owner@example.com", new PayOrderRequest
            {
                PaymentMethodId = saved.PaymentMethodId
            }, CancellationToken.None));
        Assert.AreEqual(404, useDeleted.StatusCode);
        Assert.AreEqual(0, payPal.AuthorizeCalls);
    }

    [TestMethod]
    public async Task CancelVoidsTheHoldOnceAndNeverCaptures()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient();
        var service = CreateService(db, payPal);
        var order = await CreateOrderAsync(service, db, "shopper@example.com");
        await service.PayAsync(order.OrderId, "shopper@example.com", new PayOrderRequest { Card = TestCard() }, CancellationToken.None);

        var cancelled = await service.CancelAsync(order.OrderId, CancellationToken.None);
        var replay = await service.CancelAsync(order.OrderId, CancellationToken.None);

        Assert.AreEqual("Cancelled", cancelled.PaymentStatus);
        Assert.AreEqual("Cancelled", replay.FulfillmentStatus);
        Assert.AreEqual(1, payPal.VoidCalls);
        Assert.AreEqual(0, payPal.CaptureCalls);
    }

    [TestMethod]
    public async Task FulfilRenewsAnAuthorizationOutsideTheHonorPeriodBeforeCapture()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient { AuthorizationCreatedAt = DateTimeOffset.UtcNow.AddDays(-4) };
        var service = CreateService(db, payPal);
        var order = await CreateOrderAsync(service, db, "shopper@example.com");
        await service.PayAsync(order.OrderId, "shopper@example.com", new PayOrderRequest { Card = TestCard() }, CancellationToken.None);

        var fulfilled = await service.FulfilAsync(order.OrderId, CancellationToken.None);

        Assert.AreEqual("Fulfilled", fulfilled.FulfillmentStatus);
        Assert.AreEqual(1, payPal.ReauthorizeCalls);
        Assert.AreEqual(1, payPal.CaptureCalls);
        StringAssert.EndsWith(fulfilled.AuthorizationId, "-R");
    }

    [TestMethod]
    public async Task FulfilExplainsWhenAuthorizationIsOutsidePayPalsRenewalWindow()
    {
        await using var db = CreateContext();
        var payPal = new FakePayPalClient { AuthorizationCreatedAt = DateTimeOffset.UtcNow.AddDays(-30) };
        var service = CreateService(db, payPal);
        var order = await CreateOrderAsync(service, db, "shopper@example.com");
        await service.PayAsync(order.OrderId, "shopper@example.com", new PayOrderRequest { Card = TestCard() }, CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.FulfilAsync(order.OrderId, CancellationToken.None));

        Assert.AreEqual("AUTHORIZATION_CANNOT_BE_RENEWED", error.Code);
        StringAssert.Contains(error.Message, "Ask the shopper to pay again");
        Assert.AreEqual(0, payPal.ReauthorizeCalls);
        Assert.AreEqual(0, payPal.CaptureCalls);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CatalogContext(options);
    }

    private static CommercePaymentService CreateService(CatalogContext db, FakePayPalClient payPal) =>
        new(db, payPal, Options.Create(new PayPalOptions
        {
            ClientId = "not-a-secret",
            ClientSecret = "not-a-secret",
            Environment = "Sandbox",
            Currency = "USD"
        }), new OrderOperationLock());

    private static async Task<CreateOrderResponse> CreateOrderAsync(
        CommercePaymentService service,
        CatalogContext db,
        string buyer)
    {
        var item = new CatalogItem(1, 1, "test", "Test item", 10m, "test.png");
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync();
        return await service.CreateOrderAsync(buyer, new CreateOrderRequest
        {
            Items = new List<CreateOrderItemRequest>
            {
                new() { CatalogItemId = item.Id, Quantity = 1 }
            },
            ShipToAddress = new AddressRequest
            {
                Street = "1 Main St",
                City = "San Jose",
                State = "CA",
                Country = "US",
                ZipCode = "95131"
            }
        }, CancellationToken.None);
    }

    private static CardRequest TestCard() => new()
    {
        Number = "4111 1111 1111 1111",
        Expiry = "2035-12",
        SecurityCode = "123",
        Name = "Test Shopper",
        BillingAddress = new BillingAddressRequest
        {
            AddressLine1 = "1 Main St",
            City = "San Jose",
            State = "CA",
            PostalCode = "95131",
            CountryCode = "US"
        }
    };

    private sealed class FakePayPalClient : IPayPalClient
    {
        public int AuthorizeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public int VoidCalls { get; private set; }
        public int DeleteTokenCalls { get; private set; }
        public int ReauthorizeCalls { get; private set; }
        public DateTimeOffset AuthorizationCreatedAt { get; init; } = DateTimeOffset.UtcNow;

        public Task<PayPalAuthorization> AuthorizeOrderAsync(int orderId, string integrationId, string invoiceId, decimal amount, string currency,
            PayPalCardDetails? card, string? vaultId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            return Task.FromResult(new PayPalAuthorization(
                $"ORDER-{orderId}", "COMPLETED", $"AUTH-{orderId}", "CREATED", amount,
                AuthorizationCreatedAt, AuthorizationCreatedAt.AddDays(29), "VISA", "1111"));
        }

        public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            ReauthorizeCalls++;
            return Task.FromResult(new PayPalAuthorization(string.Empty, "COMPLETED", authorizationId + "-R", "CREATED",
                amount, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), null, null));
        }

        public Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            CaptureCalls++;
            return Task.FromResult(new PayPalCapture("CAPTURE-1", "COMPLETED", amount, 0.50m,
                amount - 0.50m, DateTimeOffset.UtcNow));
        }

        public Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalCapture(captureId, "COMPLETED", 10m, 0.50m, 9.50m, DateTimeOffset.UtcNow));

        public Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
        {
            VoidCalls++;
            return Task.CompletedTask;
        }

        public Task<PayPalRefund> RefundAsync(string captureId, decimal amount, string currency,
            string requestId, CancellationToken cancellationToken)
        {
            RefundCalls++;
            return Task.FromResult(new PayPalRefund($"REFUND-{RefundCalls}", "COMPLETED", amount));
        }

        public Task<PayPalSavedCard> SaveCardAsync(string merchantCustomerId, string? payPalCustomerId,
            PayPalCardDetails card, CancellationToken cancellationToken) =>
            Task.FromResult(new PayPalSavedCard("TOKEN-1", "CUSTOMER-1", "VISA", "1111", card.Expiry, card.Name));

        public Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
        {
            DeleteTokenCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PayPalTransaction>> GetTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PayPalTransaction>>(Array.Empty<PayPalTransaction>());
    }
}
