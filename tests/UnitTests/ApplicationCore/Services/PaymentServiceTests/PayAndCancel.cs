using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PayAndCancel : PaymentServiceTestBase
{
    private static readonly CardPaymentDetails Card = new("4111111111111111", "2028-11", "123", "Demo User", "US");

    [Fact]
    public async Task DoubleClickPayReturnsExistingHoldWithoutReauthorizing()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);

        var result = await CreateService().PayWithCardAsync(BuyerId, OrderId, Card, CancellationToken.None);

        Assert.Same(payment, result);
        await Gateway.DidNotReceive().AuthorizeCardPaymentAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAuthorizesOrderTotalAndMarksStates()
    {
        var order = NewOrder(unitPrice: 8.5m, units: 2);
        GivenOrder(order);
        Gateway.AuthorizeCardPaymentAsync(OrderId, 17m, "USD", Card, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationResult("PAYPAL-ORDER-9", "AUTH-9", "CREATED", System.DateTimeOffset.UtcNow.AddDays(29)));

        var payment = await CreateService().PayWithCardAsync(BuyerId, OrderId, Card, CancellationToken.None);

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal("AUTH-9", payment.AuthorizationId);
        Assert.Equal(17m, payment.Amount);
    }

    [Fact]
    public async Task ShopperCannotPayAnotherShoppersOrder()
    {
        GivenOrder(NewOrder());

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateService().PayWithCardAsync("someone-else@example.com", OrderId, Card, CancellationToken.None));
    }

    [Fact]
    public async Task SavedCardOfAnotherShopperIsRejected()
    {
        var foreignCard = new Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate.SavedCard("other@example.com", "TOKEN-1", null, "VISA", "1111", "2028-11");
        SavedCardRepository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(foreignCard);

        await Assert.ThrowsAsync<OrderStateException>(
            () => CreateService().PayWithSavedCardAsync(BuyerId, OrderId, 7, CancellationToken.None));
    }

    [Fact]
    public async Task CancelVoidsTheHoldAndNoMoneyMoves()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        GivenOrder(order);
        GivenPayment(payment);

        var result = await CreateService().CancelOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        await Gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelOfFulfilledOrderIsRejectedInFavorOfRefund()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        payment.MarkCaptured("CAP-1", 20m, 1.10m, 18.90m);
        order.MarkFulfilled();
        GivenOrder(order);

        await Assert.ThrowsAsync<OrderStateException>(
            () => CreateService().CancelOrderAsync(OrderId, CancellationToken.None));
        await Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
