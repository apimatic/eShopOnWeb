using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class AuthorizePayment
{
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Buyer> _buyerRepository = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly OrderPaymentService _sut;

    public AuthorizePayment()
    {
        _sut = new OrderPaymentService(_orderRepository, _buyerRepository, _gateway, new PaymentSettings { Currency = "USD" });
    }

    private static CardDetails AnyCard() => new("4111111111111111", 12, 2030, "123", "Jane Doe",
        new Address("123 Main St", "Kent", "OH", "US", "44240"));

    [Fact]
    public async Task ThrowsWhenNeitherCardNorSavedCardProvided()
    {
        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            _sut.AuthorizePaymentAsync(1, "buyer-1", null, null));
    }

    [Fact]
    public async Task ThrowsWhenBothCardAndSavedCardProvided()
    {
        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            _sut.AuthorizePaymentAsync(1, "buyer-1", AnyCard(), 5));
    }

    [Fact]
    public async Task ThrowsOrderNotFoundWhenCallerDoesNotOwnTheOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        // OrderBuilder's order belongs to "12345"; a different buyer must not see or use it.
        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            _sut.AuthorizePaymentAsync(order.Id, "someone-else", AnyCard(), null));

        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task DoubleClickOnAnAlreadyAuthorizedOrderReturnsExistingPaymentWithoutCallingGateway()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.AttachPayment(new Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.Payment(order.Id, order.Total(), "USD"));
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var result = await _sut.AuthorizePaymentAsync(order.Id, order.BuyerId, AnyCard(), null);

        Assert.Same(order.Payment, result);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulCardAuthorizationAttachesPaymentAndPersistsTheOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.AuthorizeWithCardAsync(order.Total(), "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(new AuthorizationResult("paypal-order-1", "auth-1", "CREATED", null));

        var payment = await _sut.AuthorizePaymentAsync(order.Id, order.BuyerId, AnyCard(), null);

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Equal("auth-1", payment.AuthorizationId);
        await _orderRepository.Received().UpdateAsync(order, default);
    }

    [Fact]
    public async Task SavedCardNotBelongingToBuyerThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _buyerRepository.FirstOrDefaultAsync(Arg.Any<BuyerWithPaymentMethodsSpec>(), default).Returns((Buyer?)null);

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() =>
            _sut.AuthorizePaymentAsync(order.Id, order.BuyerId, null, 999));
    }
}
