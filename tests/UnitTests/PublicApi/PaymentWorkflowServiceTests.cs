using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class PaymentWorkflowServiceTests
{
    [Fact]
    public async Task MoneyMovingOperationsAreIdempotentAndRefundsAreCapped()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12.50m, "item.png"));
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPayPalClient>();
        gateway.Currency.Returns("USD");
        gateway.CreateOrderAsync(Arg.Any<string>(), 25m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalOrderResult("ORDER-1", "CREATED"));
        var authorization = new PayPalAuthorizationResult("AUTH-1", "CREATED", 25m, "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        gateway.AuthorizeOrderAsync("ORDER-1", Arg.Any<PayPalCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(authorization);
        gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>()).Returns(authorization);
        var capture = new PayPalCaptureResult("CAPTURE-1", "COMPLETED", 25m, "USD", .75m, 24.25m,
            DateTimeOffset.UtcNow);
        gateway.CaptureAsync("AUTH-1", 25m, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(capture);
        gateway.GetCaptureAsync("CAPTURE-1", Arg.Any<CancellationToken>()).Returns(capture);
        gateway.RefundAsync("CAPTURE-1", 5m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REFUND-1", "COMPLETED", 5m, DateTimeOffset.UtcNow));

        var service = new PaymentWorkflowService(db, gateway, new PaymentOperationLock());
        var created = await service.CreateOrderAsync("shopper", new CreateOrderRequest
        {
            Items = new() { new CreateOrderItemRequest { CatalogItemId = 1, Quantity = 2 } },
            ShippingAddress = Address()
        }, CancellationToken.None);
        var card = new PayOrderRequest { Card = Card() };

        var paid = await service.PayAsync("shopper", created.OrderId, card, CancellationToken.None);
        var paidAgain = await service.PayAsync("shopper", created.OrderId, card, CancellationToken.None);
        var fulfilled = await service.FulfilAsync(created.OrderId, CancellationToken.None);
        var fulfilledAgain = await service.FulfilAsync(created.OrderId, CancellationToken.None);
        var refundRequest = new RefundOrderRequest { Amount = 5m, IdempotencyKey = "same-key" };
        var refund = await service.RefundAsync("shopper", created.OrderId, refundRequest, CancellationToken.None);
        var refundAgain = await service.RefundAsync("shopper", created.OrderId, refundRequest, CancellationToken.None);

        Assert.Equal(paid.Payment.AuthorizationId, paidAgain.Payment.AuthorizationId);
        Assert.Equal("COMPLETED", fulfilled.Payment.CaptureStatus);
        Assert.Equal(fulfilled.Payment.CaptureId, fulfilledAgain.Payment.CaptureId);
        Assert.Equal(refund.RefundId, refundAgain.RefundId);
        Assert.Equal(5m, refundAgain.TotalRefunded);
        await gateway.Received(1).AuthorizeOrderAsync("ORDER-1", Arg.Any<PayPalCard>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).CaptureAsync("AUTH-1", 25m, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gateway.Received(1).RefundAsync("CAPTURE-1", 5m, Arg.Any<string>(), Arg.Any<CancellationToken>());

        var tooMuch = await Assert.ThrowsAsync<PaymentApiException>(() => service.RefundAsync("shopper",
            created.OrderId, new RefundOrderRequest { Amount = 21m, IdempotencyKey = "new-key" },
            CancellationToken.None));
        Assert.Equal(409, tooMuch.StatusCode);
    }

    [Fact]
    public async Task SavedPaymentMethodsRemainShopperScopedAndDeletionDisablesReuse()
    {
        await using var db = CreateContext();
        var gateway = Substitute.For<IPayPalClient>();
        gateway.Currency.Returns("USD");
        gateway.CreatePaymentTokenAsync(Arg.Any<string>(), Arg.Any<PayPalCard>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new PayPalPaymentTokenResult("TOKEN-1", "VISA", "1111", "2030-12"));
        var service = new PaymentWorkflowService(db, gateway, new PaymentOperationLock());

        var saved = await service.SavePaymentMethodAsync("shopper-a", Card(), CancellationToken.None);
        Assert.Single(await service.GetPaymentMethodsAsync("shopper-a", CancellationToken.None));
        Assert.Empty(await service.GetPaymentMethodsAsync("shopper-b", CancellationToken.None));
        await Assert.ThrowsAsync<PaymentApiException>(() =>
            service.DeletePaymentMethodAsync("shopper-b", saved.PaymentMethodId, CancellationToken.None));

        await service.DeletePaymentMethodAsync("shopper-a", saved.PaymentMethodId, CancellationToken.None);
        Assert.Empty(await service.GetPaymentMethodsAsync("shopper-a", CancellationToken.None));
        await gateway.Received(1).DeletePaymentTokenAsync("TOKEN-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilmentRenewsAnAuthorizationOutsideItsHonorPeriod()
    {
        await using var db = CreateContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 10m, "item.png"));
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPayPalClient>();
        gateway.Currency.Returns("USD");
        gateway.CreateOrderAsync(Arg.Any<string>(), 10m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalOrderResult("ORDER-2", "CREATED"));
        var stale = new PayPalAuthorizationResult("AUTH-2", "CREATED", 10m, "USD",
            DateTimeOffset.UtcNow.AddDays(-4), DateTimeOffset.UtcNow.AddDays(-4),
            DateTimeOffset.UtcNow.AddDays(25));
        var renewed = stale with { UpdateTime = DateTimeOffset.UtcNow };
        gateway.AuthorizeOrderAsync("ORDER-2", Arg.Any<PayPalCard>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(stale);
        gateway.GetAuthorizationAsync("AUTH-2", Arg.Any<CancellationToken>()).Returns(stale);
        gateway.ReauthorizeAsync("AUTH-2", 10m, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(renewed);
        gateway.CaptureAsync("AUTH-2", 10m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAPTURE-2", "COMPLETED", 10m, "USD", .50m, 9.50m,
                DateTimeOffset.UtcNow));
        var service = new PaymentWorkflowService(db, gateway, new PaymentOperationLock());
        var order = await service.CreateOrderAsync("shopper", new CreateOrderRequest
        {
            Items = new() { new CreateOrderItemRequest { CatalogItemId = 1, Quantity = 1 } },
            ShippingAddress = Address()
        }, CancellationToken.None);
        await service.PayAsync("shopper", order.OrderId, new PayOrderRequest { Card = Card() },
            CancellationToken.None);

        var result = await service.FulfilAsync(order.OrderId, CancellationToken.None);

        Assert.Equal("COMPLETED", result.Payment.CaptureStatus);
        await gateway.Received(1).ReauthorizeAsync("AUTH-2", 10m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static CatalogContext CreateContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ShippingAddressRequest Address() => new()
    {
        Street = "1 Main St", City = "San Jose", State = "CA", Country = "US", ZipCode = "95131"
    };

    private static CardRequest Card() => new()
    {
        Name = "Shopper", Number = "test-card-input", Expiry = "2030-12", SecurityCode = "123",
        BillingAddress = new CardBillingAddressRequest
        {
            AddressLine1 = "1 Main St", City = "San Jose", State = "CA", PostalCode = "95131",
            CountryCode = "US"
        }
    };
}
