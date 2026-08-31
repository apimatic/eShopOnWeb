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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private const string BuyerId = "demouser@microsoft.com";
    private const string OtherBuyerId = "other@microsoft.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedPaymentMethod> _methods = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private PaymentService CreateService() => new PaymentService(_orders, _items, _payments, _methods, _gateway,
        _uriComposer, Options.Create(new PaymentSettings { Currency = "USD" }));

    private static Order NewOrder(string buyerId)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Mug", "uri"), 25.50m, 2);
        return new Order(buyerId, new Address("1 Main St", "Seattle", "WA", "US", "98101"), new List<OrderItem> { item });
    }

    private Payment NewAuthorizedPayment(int orderId = 1, string buyerId = BuyerId)
        => new Payment(orderId, buyerId, "PAYPAL-ORDER-1", "AUTH-1", "CREATED",
            DateTimeOffset.UtcNow.AddDays(3), 51.00m, "USD");

    private Payment NewCapturedPayment(int orderId = 1, string buyerId = BuyerId)
    {
        var payment = NewAuthorizedPayment(orderId, buyerId);
        payment.MarkCaptured("CAPTURE-1", 51.00m, 1.81m, 49.19m, DateTimeOffset.UtcNow);
        return payment;
    }

    private void ArrangeOrder(Order order)
    {
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(order);
    }

    private void ArrangePayments(params Payment[] payments)
    {
        _payments.ListAsync(Arg.Any<PaymentsByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment>(payments));
        _payments.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
            .Returns(x => x.Arg<Payment>());
        _gateway.AuthorizeAsync(Arg.Any<string?>(), Arg.Any<CardDetails?>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PAYPAL-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    [Fact]
    public async Task PayAuthorizesOrderTotalOnceAcrossDoubleClick()
    {
        var order = NewOrder(BuyerId);
        var existing = NewAuthorizedPayment();
        ArrangeOrder(order);
        ArrangePayments(existing);
        var service = CreateService();

        var result = await service.PayOrderAsync(BuyerId, 1, new CardDetails("4111111111111111", "2030-12", "123", "Demo", null), null);

        Assert.Same(existing, result);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<string?>(), Arg.Any<CardDetails?>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayCreatesHoldForOrderTotal()
    {
        var order = NewOrder(BuyerId);
        ArrangeOrder(order);
        ArrangePayments();
        var service = CreateService();

        var payment = await service.PayOrderAsync(BuyerId, 1, new CardDetails("4111111111111111", "2030-12", "123", "Demo", null), null);

        Assert.Equal(51.00m, payment.Amount);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<string?>(), Arg.Any<CardDetails?>(), 51.00m, "USD",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayRejectsAnotherShoppersOrder()
    {
        ArrangeOrder(NewOrder(OtherBuyerId));
        ArrangePayments();
        var service = CreateService();

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            service.PayOrderAsync(BuyerId, 1, new CardDetails("4111111111111111", "2030-12", "123", "Demo", null), null));
    }

    [Fact]
    public async Task PayWithSavedCardChecksOwnership()
    {
        ArrangeOrder(NewOrder(BuyerId));
        ArrangePayments();
        _methods.GetByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(new SavedPaymentMethod(OtherBuyerId, "VAULT-1", "VISA", "1111", "2030-12", "Other"));
        var service = CreateService();

        await Assert.ThrowsAsync<SavedPaymentMethodNotFoundException>(() =>
            service.PayOrderAsync(BuyerId, 1, null, 7));
    }

    [Fact]
    public async Task FulfilCapturesAndRecordsFeeAndNet()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        ArrangeOrder(order);
        ArrangePayments(payment);
        _gateway.CaptureAsync("AUTH-1", 51.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAPTURE-1", "COMPLETED", 51.00m, 1.81m, 49.19m, "USD"));
        var service = CreateService();

        var result = await service.FulfilOrderAsync(1);

        Assert.Equal("CAPTURE-1", result.CaptureId);
        Assert.Equal(1.81m, result.CaptureFee);
        Assert.Equal(49.19m, result.CaptureNetAmount);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task FulfilTwiceCapturesOnce()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        var payment = NewCapturedPayment();
        ArrangeOrder(order);
        ArrangePayments(payment);
        var service = CreateService();

        var result = await service.FulfilOrderAsync(1);

        Assert.Same(payment, result);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationBeforeCapturing()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        var stale = new Payment(1, BuyerId, "PAYPAL-ORDER-1", "AUTH-OLD", "CREATED",
            DateTimeOffset.UtcNow.AddDays(-1), 51.00m, "USD");
        ArrangeOrder(order);
        ArrangePayments(stale);
        _gateway.ReauthorizeAsync("AUTH-OLD", 51.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH-NEW", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-NEW", 51.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAPTURE-1", "COMPLETED", 51.00m, 1.81m, 49.19m, "USD"));
        var service = CreateService();

        var result = await service.FulfilOrderAsync(1);

        Assert.Equal("AUTH-NEW", result.AuthorizationId);
        Assert.Equal("CAPTURE-1", result.CaptureId);
    }

    [Fact]
    public async Task CancelVoidsHeldFunds()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment();
        ArrangeOrder(order);
        ArrangePayments(payment);
        var service = CreateService();

        var result = await service.CancelOrderAsync(1);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        await _gateway.Received(1).VoidAuthorizationAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundUnderSameKeyRefundsOnce()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = NewCapturedPayment();
        payment.AddRefund("PAYPAL-REFUND-1", 10m, "COMPLETED", "key-A");
        ArrangeOrder(order);
        ArrangePayments(payment);
        var service = CreateService();

        var result = await service.RefundOrderAsync(BuyerId, 1, 10m, "key-A");

        Assert.Equal("PAYPAL-REFUND-1", result.PayPalRefundId);
        Assert.Equal(10m, payment.RefundedAmount);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundNeverExceedsCapturedAmount()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = NewCapturedPayment();
        payment.AddRefund("PAYPAL-REFUND-1", 40m, "COMPLETED", "key-A");
        ArrangeOrder(order);
        ArrangePayments(payment);
        var service = CreateService();

        await Assert.ThrowsAsync<RefundExceedsCapturedAmountException>(() =>
            service.RefundOrderAsync(BuyerId, 1, 20m, "key-B"));
    }

    [Fact]
    public async Task DistinctPartialRefundsAreBothAllowed()
    {
        var order = NewOrder(BuyerId);
        order.MarkPaymentAuthorized();
        order.MarkFulfilled();
        var payment = NewCapturedPayment();
        ArrangeOrder(order);
        ArrangePayments(payment);
        _gateway.RefundCaptureAsync("CAPTURE-1", 10m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("PAYPAL-REFUND-1", "COMPLETED", 10m, "USD"));
        _gateway.RefundCaptureAsync("CAPTURE-1", 5m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("PAYPAL-REFUND-2", "COMPLETED", 5m, "USD"));
        var service = CreateService();

        await service.RefundOrderAsync(BuyerId, 1, 10m, "key-A");
        await service.RefundOrderAsync(BuyerId, 1, 5m, "key-B");

        Assert.Equal(15m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }
}
