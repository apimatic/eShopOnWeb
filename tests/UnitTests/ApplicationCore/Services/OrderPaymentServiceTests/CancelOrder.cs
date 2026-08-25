using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class CancelOrder
{
    private const string BuyerId = "buyer@test.com";
    private const int OrderId = 1;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Buyer> _buyerRepo = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _paymentRepo, _catalogRepo, _buyerRepo, _gateway, "USD");

    private static Order NewOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        return new Order(BuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
    }

    [Fact]
    public async Task CancellingUnpaidOrderDoesNotCallGateway()
    {
        var order = NewOrder();
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().CancelOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingAuthorizedOrderVoidsTheHold()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = new Payment(OrderId, order.Total(), "USD");
        payment.MarkAuthorized("pp-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().CancelOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        await _gateway.Received(1).VoidAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CannotCancelAFulfilledOrder()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => CreateService().CancelOrderAsync(OrderId, CancellationToken.None));
    }

    [Fact]
    public async Task CancellingAlreadyCancelledOrderIsIdempotent()
    {
        var order = NewOrder();
        order.MarkCancelled();
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().CancelOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
