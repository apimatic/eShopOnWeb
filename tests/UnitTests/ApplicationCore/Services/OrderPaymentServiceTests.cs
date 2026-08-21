using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IPaymentSettings _settings = Substitute.For<IPaymentSettings>();
    private readonly OrderPaymentService _sut;

    public OrderPaymentServiceTests()
    {
        _settings.Currency.Returns("USD");
        _sut = new OrderPaymentService(_orders, _methods, _gateway, _settings);
    }

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_DoesNotCallGatewayAgain()
    {
        var order = AuthorizedOrder();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var result = await _sut.PayAsync(order.Id, order.BuyerId, Card(), null, default);

        Assert.Equal(OrderPaymentStatus.Authorized, result.PaymentStatus);
        await _gateway.DidNotReceiveWithAnyArgs().AuthorizeCardAsync(default, default, default!, default!, default!, default);
    }

    [Fact]
    public async Task Refund_RejectsAmountAboveRemaining()
    {
        var order = FulfilledOrder(captured: 10m);
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            _sut.RefundAsync(order.Id, order.BuyerId, "key-1", 10.01m, default));

        Assert.Equal(400, ex.StatusCode);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Refund_SameIdempotencyKey_DoesNotRefundTwice()
    {
        var order = FulfilledOrder(captured: 10m);
        var existing = new OrderRefund("refund-1", "same-key", 4m, "COMPLETED");
        order.Payment!.AddRefund(existing);
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var result = await _sut.RefundAsync(order.Id, order.BuyerId, "same-key", 4m, default);

        Assert.Equal("refund-1", result.RefundId);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Cancel_AfterFulfil_Fails()
    {
        var order = FulfilledOrder(captured: 10m);
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var ex = await Assert.ThrowsAsync<PaymentException>(() => _sut.CancelAsync(order.Id, default));

        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Shopper_CannotActOnSomeoneElsesOrder()
    {
        var order = AuthorizedOrder();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var ex = await Assert.ThrowsAsync<PaymentException>(() =>
            _sut.PayAsync(order.Id, "other-buyer", Card(), null, default));

        Assert.Equal(404, ex.StatusCode);
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = order.EnsurePayment();
        payment.ApplyAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, null, "USD");
        order.MarkAuthorized();
        return order;
    }

    private static Order FulfilledOrder(decimal captured)
    {
        var order = AuthorizedOrder();
        order.Payment!.ApplyCapture("CAP-1", "COMPLETED", captured, 0.30m, captured - 0.30m, "COMPLETED");
        order.MarkFulfilled();
        return order;
    }

    private static CardDetails Card() => new("4111111111111111", "2027-12", "123", "Test Shopper",
        new BillingAddress("US", "1 Main", null, "Kent", "OH", "44240"));
}
