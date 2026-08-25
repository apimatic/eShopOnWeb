using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class RefundOrder
{
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Buyer> _buyerRepository = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly OrderPaymentService _sut;

    public RefundOrder()
    {
        _sut = new OrderPaymentService(_orderRepository, _buyerRepository, _gateway, new PaymentSettings { Currency = "USD" });
    }

    private static Order FulfilledOrder(decimal capturedAmount)
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment(order.Id, capturedAmount, "USD");
        payment.RecordAuthorization("paypal-order-1", "auth-1", "CREATED", null, null);
        payment.RecordCapture("capture-1", "COMPLETED", capturedAmount, 1m, capturedAmount - 1m, DateTimeOffset.UtcNow);
        order.AttachPayment(payment);
        order.MarkFulfilled();
        return order;
    }

    [Fact]
    public async Task ThrowsWhenOrderHasNotBeenFulfilled()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => _sut.RefundOrderAsync(order.Id, order.BuyerId, null, "key-1"));
    }

    [Fact]
    public async Task RefundExceedingTheCapturedAmountThrows()
    {
        var order = FulfilledOrder(100m);
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => _sut.RefundOrderAsync(order.Id, order.BuyerId, 150m, "key-1"));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OmittingTheAmountRefundsTheFullRemainingBalance()
    {
        var order = FulfilledOrder(100m);
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.RefundAsync("capture-1", 100m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", 100m));

        var refund = await _sut.RefundOrderAsync(order.Id, order.BuyerId, null, "key-1");

        Assert.Equal(100m, refund.Amount);
        Assert.Equal(PaymentStatus.Refunded, order.Payment!.Status);
    }

    [Fact]
    public async Task RepeatingTheSameIdempotencyKeyReturnsTheSameRefundWithoutCallingPayPalAgain()
    {
        var order = FulfilledOrder(100m);
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.RefundAsync("capture-1", 40m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", 40m));

        var first = await _sut.RefundOrderAsync(order.Id, order.BuyerId, 40m, "key-1");
        var second = await _sut.RefundOrderAsync(order.Id, order.BuyerId, 40m, "key-1");

        Assert.Same(first, second);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoDistinctPartialRefundsAreBothAllowedUpToTheCapturedAmount()
    {
        var order = FulfilledOrder(100m);
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.RefundAsync("capture-1", 40m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", 40m));
        _gateway.RefundAsync("capture-1", 60m, "USD", "key-2", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-2", "COMPLETED", 60m));

        await _sut.RefundOrderAsync(order.Id, order.BuyerId, 40m, "key-1");
        await _sut.RefundOrderAsync(order.Id, order.BuyerId, 60m, "key-2");

        Assert.Equal(100m, order.Payment!.RefundedAmount);
        Assert.Equal(PaymentStatus.Refunded, order.Payment.Status);
    }

    [Fact]
    public async Task ThrowsOrderNotFoundWhenCallerDoesNotOwnTheOrder()
    {
        var order = FulfilledOrder(100m);
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => _sut.RefundOrderAsync(order.Id, "someone-else", null, "key-1"));
    }
}
