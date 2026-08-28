using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task RepeatedPayAndRefundRequestsMoveMoneyOnce()
    {
        await using var db = NewContext();
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12.34m, "picture"));
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPayPalGateway>();
        gateway.Currency.Returns("USD");
        gateway.CreateOrderAsync(Arg.Any<int>(), 12.34m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalOrderResult("PAYPAL-ORDER", "CREATED"));
        var now = DateTimeOffset.UtcNow;
        gateway.AuthorizeAsync("PAYPAL-ORDER", Arg.Any<PaymentSource>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("AUTH", "CREATED", 12.34m, "USD", now, now.AddDays(29)));
        gateway.GetAuthorizationAsync("AUTH", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("AUTH", "CREATED", 12.34m, "USD", now, now.AddDays(29)));
        gateway.CaptureAsync("AUTH", 12.34m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAPTURE", "COMPLETED", 12.34m, "USD", .84m, 11.50m, now));
        gateway.RefundAsync("CAPTURE", 12.34m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REFUND", "COMPLETED", 12.34m, "USD"));
        var service = NewService(db, gateway);

        var created = await service.CreateOrderAsync("buyer", new[] { new CreateOrderItem(1, 1) },
            Address(), default);
        var card = Card();
        var paid = await service.PayAsync("buyer", created.OrderId, card, null, default);
        var repeatedPay = await service.PayAsync("buyer", created.OrderId, card, null, default);
        Assert.Equal(paid.AuthorizationId, repeatedPay.AuthorizationId);
        await gateway.Received(1).AuthorizeAsync("PAYPAL-ORDER", Arg.Any<PaymentSource>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var fulfilled = await service.FulfilAsync(created.OrderId, default);
        Assert.Equal("Captured", fulfilled.PaymentStatus);
        Assert.Equal(12.34m, fulfilled.CapturedAmount);
        Assert.Equal(.84m, fulfilled.PayPalFee);
        Assert.Equal(11.50m, fulfilled.NetProceeds);

        var refund = await service.RefundAsync("buyer", created.OrderId, null, "refund-key", default);
        var repeatedRefund = await service.RefundAsync("buyer", created.OrderId, null, "refund-key", default);
        Assert.Equal(refund.RefundId, repeatedRefund.RefundId);
        await gateway.Received(1).RefundAsync("CAPTURE", 12.34m, "USD", Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        var error = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.RefundAsync("buyer", created.OrderId, .01m, "another-key", default));
        Assert.Equal(PaymentErrorKind.InvalidRequest, error.Kind);
    }

    [Fact]
    public async Task SavedCardsAreIsolatedByBuyerAndDeletionRevokesLocalUse()
    {
        await using var db = NewContext();
        var gateway = Substitute.For<IPayPalGateway>();
        gateway.Currency.Returns("USD");
        gateway.SaveCardAsync(Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new VaultedCardResult("TOKEN", "CUSTOMER", "VISA", "1111", "2030-12"));
        var service = NewService(db, gateway);

        var saved = await service.SavePaymentMethodAsync("buyer-a", Card(), default);
        Assert.Equal("1111", saved.Last4);
        Assert.Single(await service.GetPaymentMethodsAsync("buyer-a", default));
        Assert.Empty(await service.GetPaymentMethodsAsync("buyer-b", default));
        var foreignDelete = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.DeletePaymentMethodAsync("buyer-b", saved.PaymentMethodId, default));
        Assert.Equal(PaymentErrorKind.NotFound, foreignDelete.Kind);
        await gateway.DidNotReceive().DeletePaymentTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await service.DeletePaymentMethodAsync("buyer-a", saved.PaymentMethodId, default);
        Assert.Empty(await service.GetPaymentMethodsAsync("buyer-a", default));
        await gateway.Received(1).DeletePaymentTokenAsync("TOKEN", Arg.Any<CancellationToken>());
    }

    private static PaymentService NewService(CatalogContext db, IPayPalGateway gateway) =>
        new(db, gateway, new PaymentOperationLock(), TimeProvider.System);

    private static CatalogContext NewContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ShippingAddress Address() => new("street", "city", "state", "US", "12345");
    private static CardDetails Card() => new("Shopper", "test-card-input", "2030-12", "123",
        new CardBillingAddress("US", "street", null, "city", "CA", "12345"));
}
