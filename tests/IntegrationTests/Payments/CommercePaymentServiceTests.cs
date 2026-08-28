#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public sealed class CommercePaymentServiceTests
{
    [Fact]
    public async Task AuthorizeCaptureAndRefundAreIdempotentAndCentExact()
    {
        await using var context = CreateContext();
        var item = new CatalogItem(1, 1, "Payment test item", "Payment test item", 100m, "picture.png");
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();
        var payPal = Substitute.For<IPayPalClient>();
        payPal.Currency.Returns("USD");
        payPal.AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CardData>(), Arg.Is<string?>(value => value == null),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new PayPalAuthorization($"ORDER-{call.ArgAt<int>(0)}", "COMPLETED",
                $"AUTH-{call.ArgAt<int>(0)}", "CREATED", call.ArgAt<decimal>(2), "USD",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        payPal.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new PayPalCapture($"CAP-{call.ArgAt<string>(1)}", "COMPLETED",
                call.ArgAt<decimal>(2), "USD", 3.20m, 96.80m));
        var refundNumber = 0;
        payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new PayPalRefund($"REF-{Interlocked.Increment(ref refundNumber)}",
                "COMPLETED", call.ArgAt<decimal>(1), "USD", DateTimeOffset.UtcNow));
        var service = new CommercePaymentService(context, payPal);
        var order = await service.CreateOrderAsync("shopper@example.test",
            new[] { new OrderLineData(item.Id, 1) }, Address(), default);
        var card = Card();

        var firstPay = await service.PayAsync(order.Id, order.BuyerId, card, null, default);
        var repeatedPay = await service.PayAsync(order.Id, order.BuyerId, card, null, default);
        Assert.Equal(PaymentStatus.Authorized, firstPay.PaymentStatus);
        Assert.Equal(firstPay.PayPalAuthorizationId, repeatedPay.PayPalAuthorizationId);
        await payPal.Received(1).AuthorizeAsync(order.Id, order.PaymentReference, 100m, Arg.Any<CardData>(), Arg.Is<string?>(value => value == null),
            $"eshop-{order.PaymentReference}-authorize", Arg.Any<CancellationToken>());

        var fulfilled = await service.FulfilAsync(order.Id, default);
        var repeatedFulfilment = await service.FulfilAsync(order.Id, default);
        Assert.Equal(PaymentStatus.Captured, fulfilled.PaymentStatus);
        Assert.Equal(100m, fulfilled.CapturedAmount);
        Assert.Equal(3.20m, fulfilled.PayPalFee);
        Assert.Equal(96.80m, fulfilled.MerchantNetAmount);
        Assert.Equal(fulfilled.PayPalCaptureId, repeatedFulfilment.PayPalCaptureId);
        await payPal.Received(1).CaptureAsync(Arg.Any<string>(), order.PaymentReference, 100m,
            $"eshop-{order.PaymentReference}-capture", Arg.Any<CancellationToken>());

        var firstRefund = await service.RefundAsync(order.Id, order.BuyerId, 40m, "return-one", default);
        var repeatedRefund = await service.RefundAsync(order.Id, order.BuyerId, 40m, "return-one", default);
        var secondRefund = await service.RefundAsync(order.Id, order.BuyerId, 60m, "return-two", default);
        Assert.Equal(firstRefund.PayPalRefundId, repeatedRefund.PayPalRefundId);
        Assert.NotEqual(firstRefund.PayPalRefundId, secondRefund.PayPalRefundId);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(100m, order.RefundedAmount);
        await payPal.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.RefundAsync(order.Id, order.BuyerId, 0.01m, "too-much", default));
    }

    [Fact]
    public async Task SavedCardIsShopperScopedReusableAndDeletedAtPayPal()
    {
        await using var context = CreateContext();
        var item = new CatalogItem(1, 1, "Vault test item", "Vault test item", 12m, "picture.png");
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();
        var payPal = Substitute.For<IPayPalClient>();
        payPal.Currency.Returns("USD");
        payPal.SaveCardAsync(Arg.Any<CardData>(), Arg.Is<string?>(value => value == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalVaultToken("TOKEN-1", "CUSTOMER-1", "VISA", "1111", "2030-12"));
        payPal.AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), 12m, Arg.Is<CardData?>(value => value == null), Arg.Is<string?>(value => value == "TOKEN-1"), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new PayPalAuthorization("ORDER-1", "COMPLETED", "AUTH-1", "CREATED",
                12m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
        var service = new CommercePaymentService(context, payPal);

        var method = await service.SavePaymentMethodAsync("owner@example.test", Card(), default);
        Assert.Equal("1111", method.LastDigits);
        Assert.DoesNotContain(context.Entry(method).Properties, property => property.Metadata.Name.Contains("Number"));
        Assert.Empty(await service.GetPaymentMethodsAsync("other@example.test", default));

        var order = await service.CreateOrderAsync("owner@example.test",
            new[] { new OrderLineData(item.Id, 1) }, Address(), default);
        await service.PayAsync(order.Id, order.BuyerId, null, method.Id, default);
        await payPal.Received(1).AuthorizeAsync(order.Id, order.PaymentReference, 12m, Arg.Is<CardData?>(value => value == null), Arg.Is<string?>(value => value == "TOKEN-1"), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.DeletePaymentMethodAsync(method.Id, "other@example.test", default));

        await service.DeletePaymentMethodAsync(method.Id, order.BuyerId, default);
        Assert.Empty(await service.GetPaymentMethodsAsync(order.BuyerId, default));
        await payPal.Received(1).DeletePaymentTokenAsync("TOKEN-1", Arg.Any<CancellationToken>());
        var secondOrder = await service.CreateOrderAsync(order.BuyerId,
            new[] { new OrderLineData(item.Id, 1) }, Address(), default);
        await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.PayAsync(secondOrder.Id, secondOrder.BuyerId, null, method.Id, default));
    }

    [Fact]
    public async Task FulfilRenewsAnAuthorizationOutsideTheHonorPeriod()
    {
        await using var context = CreateContext();
        var item = new CatalogItem(1, 1, "Renewal test item", "Renewal test item", 25m, "picture.png");
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync();
        var payPal = Substitute.For<IPayPalClient>();
        payPal.Currency.Returns("USD");
        payPal.AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), 25m, Arg.Any<CardData>(),
                Arg.Is<string?>(value => value == null), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorization("ORDER-RENEW", "COMPLETED", "AUTH-OLD", "CREATED",
                25m, "USD", DateTimeOffset.UtcNow.AddDays(-4), DateTimeOffset.UtcNow.AddDays(25)));
        payPal.ReauthorizeAsync("AUTH-OLD", 25m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorization(string.Empty, string.Empty, "AUTH-NEW", "CREATED",
                25m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        payPal.CaptureAsync("AUTH-NEW", Arg.Any<string>(), 25m, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new PayPalCapture("CAP-RENEW", "COMPLETED", 25m, "USD", 1m, 24m));
        var service = new CommercePaymentService(context, payPal);
        var order = await service.CreateOrderAsync("shopper@example.test",
            new[] { new OrderLineData(item.Id, 1) }, Address(), default);

        await service.PayAsync(order.Id, order.BuyerId, Card(), null, default);
        var fulfilled = await service.FulfilAsync(order.Id, default);

        Assert.Equal("AUTH-NEW", fulfilled.PayPalAuthorizationId);
        Assert.Equal("CAP-RENEW", fulfilled.PayPalCaptureId);
        await payPal.Received(1).ReauthorizeAsync("AUTH-OLD", 25m,
            $"eshop-{order.PaymentReference}-reauthorize", Arg.Any<CancellationToken>());
        await payPal.Received(1).CaptureAsync("AUTH-NEW", order.PaymentReference, 25m,
            $"eshop-{order.PaymentReference}-capture", Arg.Any<CancellationToken>());
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new CatalogContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static Address Address() => new("1 Test Street", "Test City", "CA", "US", "12345");

    private static CardData Card() => new("not-retained", "2030-12", "000", "Test Shopper",
        new BillingAddressData("1 Test Street", null, "Test City", "CA", "12345", "US"));
}
