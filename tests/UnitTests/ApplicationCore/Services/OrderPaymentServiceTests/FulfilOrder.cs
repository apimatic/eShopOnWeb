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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class FulfilOrder
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

    private static Order NewAuthorizedOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        var order = new Order(BuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
        order.MarkPaymentAuthorized();
        return order;
    }

    private static Payment NewAuthorizedPayment(decimal amount, DateTimeOffset? expiresAt = null)
    {
        var payment = new Payment(OrderId, amount, "USD");
        payment.MarkAuthorized("pp-order-1", "auth-1", "CREATED", expiresAt ?? DateTimeOffset.UtcNow.AddDays(3), null);
        return payment;
    }

    [Fact]
    public async Task CapturesAndMarksOrderFulfilled()
    {
        var order = NewAuthorizedOrder();
        var payment = NewAuthorizedPayment(order.Total());

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("capture-1", "COMPLETED", payment.Amount, 1m, payment.Amount - 1m));

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("capture-1", result.PayPalCaptureId);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task RenewsStaleAuthorizationBeforeCapturing()
    {
        var order = NewAuthorizedOrder();
        var payment = NewAuthorizedPayment(order.Total(), DateTimeOffset.UtcNow.AddDays(-1)); // already expired

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.ReauthorizeAsync("auth-1", payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReauthorizeResult("auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("auth-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("capture-1", "COMPLETED", payment.Amount, 1m, payment.Amount - 1m));

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        await _gateway.Received(1).ReauthorizeAsync("auth-1", payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CaptureAsync("auth-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(PaymentStatus.Captured, result.Status);
    }

    [Fact]
    public async Task SurfacesUnrenewableAuthorizationAsOperatorActionableFailure()
    {
        var order = NewAuthorizedOrder();
        var payment = NewAuthorizedPayment(order.Total(), DateTimeOffset.UtcNow.AddDays(-1));

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReauthorizeResult>(new AuthorizationNotRenewableException("beyond the reauthorization window")));

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() => CreateService().FulfilOrderAsync(OrderId, CancellationToken.None));

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status); // order not marked fulfilled on failure
    }

    [Fact]
    public async Task IsIdempotentWhenAlreadyCaptured()
    {
        var order = NewAuthorizedOrder();
        order.MarkFulfilled();
        var payment = NewAuthorizedPayment(order.Total());
        payment.MarkCaptured("capture-1", "COMPLETED", payment.Amount, 1m, payment.Amount - 1m);

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var result = await CreateService().FulfilOrderAsync(OrderId, CancellationToken.None);

        Assert.Same(payment, result);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenOrderNeverAuthorized()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        var order = new Order(BuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
        var payment = new Payment(OrderId, order.Total(), "USD");

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => CreateService().FulfilOrderAsync(OrderId, CancellationToken.None));
    }
}
