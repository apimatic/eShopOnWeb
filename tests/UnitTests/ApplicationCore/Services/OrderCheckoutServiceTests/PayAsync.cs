using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderCheckoutServiceTests;

public class PayAsync
{
    private readonly IRepository<Order> _mockOrderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _mockItemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Buyer> _mockBuyerRepo = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _mockGateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _mockUriComposer = Substitute.For<IUriComposer>();
    private readonly PaymentSettings _paymentSettings = new() { Currency = "USD" };

    private static Order CreateUnpaidOrder(string buyerId) =>
        new(buyerId, new Address("1 St", "City", "ST", "USA", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), 17.00m, 1) });

    private OrderCheckoutService CreateSut() =>
        new(_mockOrderRepo, _mockItemRepo, _mockBuyerRepo, _mockGateway, _paymentSettings, _mockUriComposer);

    [Fact]
    public async Task ThrowsForbiddenWhenOrderBelongsToAnotherBuyer()
    {
        var order = CreateUnpaidOrder("owner@test.com");
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => CreateSut().PayAsync("attacker@test.com", order.Id, null, 1));
    }

    [Fact]
    public async Task RepeatingPayOnAlreadyAuthorizedOrderReplaysStateWithoutAuthorizingAgain()
    {
        var order = CreateUnpaidOrder("buyer@test.com");
        var payment = new OrderPayment(order.Id, "USD", 17.00m, null, "paypal-order-1", "auth-1", "CREATED",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.AttachPayment(payment);
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var card = new CardDetails("4111111111111111", "2028-04", "123", "Jane", null);
        var result = await CreateSut().PayAsync("buyer@test.com", order.Id, card, null);

        Assert.Equal("auth-1", result.Payment!.AuthorizationId);
        await _mockGateway.DidNotReceive().AuthorizeAsync(Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<CardDetails>(), Arg.Any<string?>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task ThrowsWhenPayingACancelledOrder()
    {
        var order = CreateUnpaidOrder("buyer@test.com");
        order.MarkCancelled();
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var card = new CardDetails("4111111111111111", "2028-04", "123", "Jane", null);
        await Assert.ThrowsAsync<InvalidOrderStateException>(
            () => CreateSut().PayAsync("buyer@test.com", order.Id, card, null));
    }

    [Fact]
    public async Task AuthorizesForExactOrderTotal()
    {
        var order = CreateUnpaidOrder("buyer@test.com");
        _mockOrderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _mockGateway.AuthorizeAsync(17.00m, "USD", Arg.Any<CardDetails>(), Arg.Any<string?>(), Arg.Any<string>(), default)
            .Returns(new GatewayAuthorizationResult("paypal-order-1", "auth-1", "CREATED", 17.00m, "USD",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));

        var card = new CardDetails("4111111111111111", "2028-04", "123", "Jane", null);
        var result = await CreateSut().PayAsync("buyer@test.com", order.Id, card, null);

        Assert.Equal(OrderStatus.PaymentAuthorized, result.Status);
        await _mockGateway.Received(1).AuthorizeAsync(17.00m, "USD", Arg.Any<CardDetails>(), Arg.Any<string?>(), Arg.Any<string>(), default);
    }
}
