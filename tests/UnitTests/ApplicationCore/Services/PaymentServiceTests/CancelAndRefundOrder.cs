using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class CancelAndRefundOrder
{
    private const string BuyerId = "buyer-1";
    private const int OrderId = 7;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedPaymentMethod> _savedMethodRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() => new PaymentService(
        _orderRepo, _paymentRepo, _savedMethodRepo, _gateway,
        new PayPalOptions { Currency = "USD" }, _logger);

    private static Order CreateOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://img"), 25m, 1);
        return new OrderWithId(BuyerId, new Address("Main st", "City", "ST", "Country", "12345"), new List<OrderItem> { item }, OrderId);
    }

    private static Payment CreateAuthorizedPayment(decimal amount = 25m)
    {
        var payment = new Payment(OrderId, BuyerId, amount, "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        return payment;
    }

    private void Given(Order order, Payment? payment)
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);
    }

    [Fact]
    public async Task CancelVoidsTheAuthorization()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        var payment = CreateAuthorizedPayment();
        Given(order, payment);

        var result = await CreateService().CancelAsync(OrderId, default);

        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(OrderStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task CancelIsIdempotentWhenAlreadyCancelled()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        var payment = CreateAuthorizedPayment();
        Given(order, payment);
        await CreateService().CancelAsync(OrderId, default);

        var second = await CreateService().CancelAsync(OrderId, default);

        Assert.Equal(OrderStatus.Cancelled, second.Status);
        await _gateway.Received(1).VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelThrowsWhenOrderFulfilled()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = CreateAuthorizedPayment();
        payment.MarkCaptured("CAP-1", 25m, 1.15m, 23.85m, "COMPLETED");
        Given(order, payment);

        await Assert.ThrowsAsync<PaymentStateConflictException>(() => CreateService().CancelAsync(OrderId, default));
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundReturnsExistingRecordForRepeatedIdempotencyKey()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = CreateAuthorizedPayment();
        payment.MarkCaptured("CAP-1", 25m, 1.15m, 23.85m, "COMPLETED");
        var existing = payment.AddRefund("key-1", 10m);
        existing.MarkSettled("REF-1", PaymentRefundStatus.Completed);
        payment.ApplyRefundedStatus();
        Given(order, payment);

        var refund = await CreateService().RefundAsync(BuyerId, OrderId, 10m, "key-1", null, default);

        Assert.Same(existing, refund);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountThrows()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = CreateAuthorizedPayment();
        payment.MarkCaptured("CAP-1", 25m, 1.15m, 23.85m, "COMPLETED");
        var first = payment.AddRefund("key-1", 20m);
        first.MarkSettled("REF-1", PaymentRefundStatus.Completed);
        payment.ApplyRefundedStatus();
        Given(order, payment);

        await Assert.ThrowsAsync<PaymentStateConflictException>(
            () => CreateService().RefundAsync(BuyerId, OrderId, 6m, "key-2", null, default));
    }

    [Fact]
    public async Task RefundThrowsWhenPaymentNotCaptured()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        Given(order, CreateAuthorizedPayment());

        await Assert.ThrowsAsync<PaymentStateConflictException>(
            () => CreateService().RefundAsync(BuyerId, OrderId, null, "key-1", null, default));
    }

    [Fact]
    public async Task RefundThrowsWhenOrderBelongsToAnotherBuyer()
    {
        var order = CreateOrder();
        Given(order, null);

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => CreateService().RefundAsync("someone-else", OrderId, null, "key-1", null, default));
    }

    private class OrderWithId : Order
    {
        public OrderWithId(string buyerId, Address address, List<OrderItem> items, int id)
            : base(buyerId, address, items)
        {
            Id = id;
        }
    }
}
