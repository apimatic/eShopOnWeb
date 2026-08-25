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

public class RefundOrder
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

    private static Order NewFulfilledOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        var order = new Order(BuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        return order;
    }

    private static Payment NewCapturedPayment(decimal amount)
    {
        var payment = new Payment(OrderId, amount, "USD");
        payment.MarkAuthorized("pp-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);
        payment.MarkCaptured("capture-1", "COMPLETED", amount, 1m, amount - 1m);
        return payment;
    }

    [Fact]
    public async Task FullRefundMarksPaymentRefunded()
    {
        var order = NewFulfilledOrder();
        var payment = NewCapturedPayment(order.Total());

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.RefundAsync("capture-1", null, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", payment.Amount));

        var refund = await CreateService().RefundOrderAsync(BuyerId, OrderId, null, "key-1", CancellationToken.None);

        Assert.Equal("refund-1", refund.PayPalRefundId);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public async Task RepeatingSameIdempotencyKeyDoesNotCallGatewayAgain()
    {
        var order = NewFulfilledOrder();
        var payment = NewCapturedPayment(order.Total());
        payment.AddRefund("refund-1", 20m, "COMPLETED", "key-1");

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var refund = await CreateService().RefundOrderAsync(BuyerId, OrderId, 20m, "key-1", CancellationToken.None);

        Assert.Equal("refund-1", refund.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DistinctPartialRefundsBothApply()
    {
        var order = NewFulfilledOrder();
        var payment = NewCapturedPayment(100m);

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.RefundAsync("capture-1", 40m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-1", "COMPLETED", 40m));
        _gateway.RefundAsync("capture-1", 30m, "USD", "key-2", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("refund-2", "COMPLETED", 30m));

        await CreateService().RefundOrderAsync(BuyerId, OrderId, 40m, "key-1", CancellationToken.None);
        await CreateService().RefundOrderAsync(BuyerId, OrderId, 30m, "key-2", CancellationToken.None);

        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(70m, payment.TotalRefunded);
    }

    [Fact]
    public async Task RejectsRefundExceedingRemainingAmountWithoutCallingGateway()
    {
        var order = NewFulfilledOrder();
        var payment = NewCapturedPayment(50m);

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        await Assert.ThrowsAsync<InvalidOrderStateException>(
            () => CreateService().RefundOrderAsync(BuyerId, OrderId, 60m, "key-1", CancellationToken.None));

        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CannotRefundBeforeFulfilment()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        var order = new Order(BuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
        var payment = new Payment(OrderId, order.Total(), "USD");

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        await Assert.ThrowsAsync<InvalidOrderStateException>(
            () => CreateService().RefundOrderAsync(BuyerId, OrderId, null, "key-1", CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenOrderDoesNotBelongToBuyer()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        var order = new Order("someone-else@test.com", new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => CreateService().RefundOrderAsync(BuyerId, OrderId, null, "key-1", CancellationToken.None));
    }
}
