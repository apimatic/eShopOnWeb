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

public class FulfilOrder
{
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Buyer> _buyerRepository = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly OrderPaymentService _sut;

    public FulfilOrder()
    {
        _sut = new OrderPaymentService(_orderRepository, _buyerRepository, _gateway, new PaymentSettings { Currency = "USD" });
    }

    private static Order AuthorizedOrder(DateTimeOffset? expiresAt = null)
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = new Payment(order.Id, order.Total(), "USD");
        payment.RecordAuthorization("paypal-order-1", "auth-1", "CREATED", expiresAt, null);
        order.AttachPayment(payment);
        return order;
    }

    [Fact]
    public async Task ThrowsWhenOrderIsNotAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => _sut.FulfilOrderAsync(order.Id));
    }

    [Fact]
    public async Task DoubleClickOnAnAlreadyFulfilledOrderReturnsExistingPaymentWithoutCallingGateway()
    {
        var order = AuthorizedOrder();
        order.MarkFulfilled();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);

        var result = await _sut.FulfilOrderAsync(order.Id);

        Assert.Same(order.Payment, result);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CapturesAndMarksTheOrderFulfilled()
    {
        var order = AuthorizedOrder();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("capture-1", "COMPLETED", order.Total(), 1.5m, order.Total() - 1.5m, DateTimeOffset.UtcNow));

        var payment = await _sut.FulfilOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("capture-1", payment.CaptureId);
        Assert.Equal(1.5m, payment.PayPalFeeAmount);
        await _orderRepository.Received().UpdateAsync(order, default);
    }

    [Fact]
    public async Task RenewsAStaleAuthorizationBeforeCapturing()
    {
        var order = AuthorizedOrder(expiresAt: DateTimeOffset.UtcNow.AddDays(-1));
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.ReauthorizeAsync("auth-1", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("auth-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("capture-1", "COMPLETED", order.Total(), 1m, order.Total() - 1m, DateTimeOffset.UtcNow));

        var payment = await _sut.FulfilOrderAsync(order.Id);

        await _gateway.Received(1).ReauthorizeAsync("auth-1", Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("capture-1", payment.CaptureId);
    }

    [Fact]
    public async Task RetriesOnceViaReauthorizeWhenCaptureReportsAnExpiredAuthorization()
    {
        var order = AuthorizedOrder();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CaptureResult>(new AuthorizationExpiredException("stale")));
        _gateway.ReauthorizeAsync("auth-1", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("auth-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("capture-1", "COMPLETED", order.Total(), 1m, order.Total() - 1m, DateTimeOffset.UtcNow));

        var payment = await _sut.FulfilOrderAsync(order.Id);

        Assert.Equal("capture-1", payment.CaptureId);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task SurfacesANonRenewableAuthorizationAsAnOperatorActionableError()
    {
        var order = AuthorizedOrder();
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), default).Returns(order);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CaptureResult>(new AuthorizationExpiredException("stale")));
        _gateway.ReauthorizeAsync("auth-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReauthorizationResult>(new AuthorizationNotRenewableException("PayPal: cannot renew past the reauthorization window")));

        var ex = await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() => _sut.FulfilOrderAsync(order.Id));

        Assert.Contains("cannot renew", ex.Message);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }
}
