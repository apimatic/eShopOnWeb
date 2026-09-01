using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PayOrder : OrderPaymentServiceTestBase
{
    private static readonly CardDetails TestCard = new("4111111111111111", "2030-01", "123", "Demo Buyer", null);

    [Fact]
    public async Task AuthorizesAndPersistsPayPalState()
    {
        var order = NewOrder();
        ReturnsOrder(order);
        Gateway.AuthorizePaymentAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var result = await CreateService().PayOrderAsync(BuyerId, 1, TestCard, null);

        Assert.Equal(OrderPaymentStatus.Authorized, result.PaymentStatus);
        Assert.Equal("PP-ORDER-1", result.PayPalOrderId);
        Assert.Equal("AUTH-1", result.AuthorizationId);
        await Gateway.Received(1).AuthorizePaymentAsync(
            Arg.Is<AuthorizationRequest>(r => r.Amount == 24m && r.Currency == "USD" && r.Card == TestCard
                && !string.IsNullOrEmpty(r.IdempotencyKey)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedPayReturnsExistingAuthorizationWithoutCallingGateway()
    {
        var order = NewAuthorizedOrder();
        ReturnsOrder(order);

        var result = await CreateService().PayOrderAsync(BuyerId, 1, TestCard, null);

        Assert.Equal("AUTH-1", result.AuthorizationId);
        await Gateway.DidNotReceive().AuthorizePaymentAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayForAnotherBuyersOrderThrowsNotFound()
    {
        ReturnsOrder(NewOrder(buyerId: "someone-else"));

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, TestCard, null));
    }

    [Fact]
    public async Task PayWithAnotherBuyersSavedCardThrowsNotFound()
    {
        ReturnsOrder(NewOrder());
        var otherPeoplesCard = new SavedCard("someone-else", "CUST-9", "TOKEN-9", "VISA", "1111", "2030-01");
        Cards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(otherPeoplesCard);

        await Assert.ThrowsAsync<SavedCardNotFoundException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, null, 5));
    }

    [Fact]
    public async Task PayWithSavedCardUsesItsVaultToken()
    {
        var order = NewOrder();
        ReturnsOrder(order);
        var savedCard = new SavedCard(BuyerId, "CUST-1", "TOKEN-1", "VISA", "1111", "2030-01");
        Cards.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(savedCard);
        Gateway.AuthorizePaymentAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        await CreateService().PayOrderAsync(BuyerId, 1, null, 5);

        await Gateway.Received(1).AuthorizePaymentAsync(
            Arg.Is<AuthorizationRequest>(r => r.VaultedCardTokenId == "TOKEN-1" && r.Card == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayRequiresExactlyOnePaymentSource()
    {
        ReturnsOrder(NewOrder());

        await Assert.ThrowsAsync<BadRequestException>(() => CreateService().PayOrderAsync(BuyerId, 1, null, null));
        await Assert.ThrowsAsync<BadRequestException>(() => CreateService().PayOrderAsync(BuyerId, 1, TestCard, 5));
    }
}
