using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

/// <summary>
/// Exercises the payment orchestration rules that must hold regardless of PayPal: effect-idempotency
/// on pay and refund, the over-refund guard, per-shopper ownership, cancel-releases-the-hold, and the
/// renew-a-stale-authorization-before-capture behaviour. PayPal is substituted so these run offline.
/// </summary>
public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string Currency = "USD";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Buyer> _buyers = Substitute.For<IRepository<Buyer>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IPayPalClient _payPal = Substitute.For<IPayPalClient>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();
    private readonly PayPalSettings _settings = new() { Currency = Currency, Environment = "sandbox" };

    private OrderPaymentService CreateService() =>
        new(_orders, _buyers, _items, _payPal, _uriComposer, _settings, _logger);

    private static Order NewOrder(string buyerId = Buyer, decimal unitPrice = 10m, int units = 3)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test Product", "pic.png"), unitPrice, units);
        return new Order(buyerId, new Address("1 St", "Kent", "OH", "US", "44240"), new List<OrderItem> { item });
    }

    private void ReturnsOrder(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    [Fact]
    public async Task Pay_AuthorizesHoldForOrderTotal_AndMarksAuthorized()
    {
        var order = NewOrder(); // total 30.00
        ReturnsOrder(order);
        _payPal.CreateAuthorizeOrderAsync(Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalOrderResult("PP-ORDER-1", "CREATED"));
        _payPal.AuthorizeOrderWithCardAsync(Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult("COMPLETED", "AUTH-1", "CREATED", 30m, Currency,
                DateTimeOffset.UtcNow.AddDays(3), "VISA", "1111", null, null));

        var card = new CardDetails("4111111111111111", "2030-01", "123", "T", null);
        var result = await CreateService().PayAsync(1, Buyer, new PaymentInstrument(card, null));

        Assert.Equal(OrderStatus.Authorized, result.Status);
        Assert.Equal("AUTH-1", result.Payment!.AuthorizationId);
        Assert.Equal(30m, result.Payment.AuthorizedAmount); // equals the order total to the cent
    }

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_DoesNotAuthorizeAgain()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetPayPalOrderId("PP-ORDER-1");
        payment.SetAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA", "1111", null);
        order.MarkAuthorized();
        ReturnsOrder(order);

        var card = new CardDetails("4111111111111111", "2030-01", "123", "T", null);
        await CreateService().PayAsync(1, Buyer, new PaymentInstrument(card, null));

        await _payPal.DidNotReceive().CreateAuthorizeOrderAsync(Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payPal.DidNotReceive().AuthorizeOrderWithCardAsync(Arg.Any<string>(), Arg.Any<CardDetails>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_ForAnotherShoppersOrder_IsNotFound()
    {
        var order = NewOrder("someone-else@example.com");
        ReturnsOrder(order);

        var card = new CardDetails("4111111111111111", "2030-01", "123", "T", null);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            CreateService().PayAsync(1, Buyer, new PaymentInstrument(card, null)));
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_ReauthorizesThenCaptures()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetPayPalOrderId("PP-ORDER-1");
        payment.SetAuthorization("AUTH-OLD", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA", "1111", null);
        order.MarkAuthorized();
        ReturnsOrder(order);

        // The hold reads back as expired, so the service must renew it before capturing.
        _payPal.GetAuthorizationAsync("AUTH-OLD", Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult("", "AUTH-OLD", "CREATED", 30m, Currency,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, null, null, null));
        _payPal.ReauthorizeAsync("AUTH-OLD", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult("", "AUTH-NEW", "CREATED", 30m, Currency,
                DateTimeOffset.UtcNow.AddDays(3), null, null, null, null));
        _payPal.CaptureAuthorizationAsync("AUTH-NEW", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-1", "COMPLETED", 30m, 1.17m, 28.83m, Currency));

        var result = await CreateService().FulfilAsync(1);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("CAP-1", result.Payment!.CaptureId);
        Assert.Equal(1.17m, result.Payment.PayPalFee);
        Assert.Equal(28.83m, result.Payment.NetAmount);
        await _payPal.Received(1).ReauthorizeAsync("AUTH-OLD", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payPal.Received(1).CaptureAuthorizationAsync("AUTH-NEW", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_BeforeFulfilment_VoidsHoldAndMarksCancelled()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetPayPalOrderId("PP-ORDER-1");
        payment.SetAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "VISA", "1111", null);
        order.MarkAuthorized();
        ReturnsOrder(order);

        var result = await CreateService().CancelAsync(1);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _payPal.Received(1).VoidAuthorizationAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_BeyondCapturedAmount_IsRejected()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetAuthorization("AUTH-1", "CAPTURED", null, "VISA", "1111", null);
        payment.SetCapture("CAP-1", "COMPLETED", 30m, 1.17m, 28.83m);
        order.MarkFulfilled();
        ReturnsOrder(order);

        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().RefundAsync(1, Buyer, 100m, "key-1"));
        await _payPal.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedUnderSameKey_DoesNotRefundTwice()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetAuthorization("AUTH-1", "CAPTURED", null, "VISA", "1111", null);
        payment.SetCapture("CAP-1", "COMPLETED", 30m, 1.17m, 28.83m);
        order.MarkFulfilled();
        ReturnsOrder(order);

        _payPal.RefundCaptureAsync("CAP-1", Arg.Any<decimal?>(), Currency, "key-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-1", "COMPLETED", 10m, Currency));

        var service = CreateService();
        var first = await service.RefundAsync(1, Buyer, 10m, "key-1");
        var second = await service.RefundAsync(1, Buyer, 10m, "key-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("REF-1", second.PayPalRefundId);
        await _payPal.Received(1).RefundCaptureAsync("CAP-1", Arg.Any<decimal?>(), Currency, "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctPartialRefunds_AreBothAllowed()
    {
        var order = NewOrder();
        var payment = order.StartPayment(Currency, "ESHOP-1-abc");
        payment.SetAuthorization("AUTH-1", "CAPTURED", null, "VISA", "1111", null);
        payment.SetCapture("CAP-1", "COMPLETED", 30m, 1.17m, 28.83m);
        order.MarkFulfilled();
        ReturnsOrder(order);

        _payPal.RefundCaptureAsync("CAP-1", Arg.Any<decimal?>(), Currency, "key-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-1", "COMPLETED", 10m, Currency));
        _payPal.RefundCaptureAsync("CAP-1", Arg.Any<decimal?>(), Currency, "key-2", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("REF-2", "COMPLETED", 5m, Currency));

        var service = CreateService();
        await service.RefundAsync(1, Buyer, 10m, "key-1");
        await service.RefundAsync(1, Buyer, 5m, "key-2");

        Assert.Equal(15m, payment.TotalRefunded());
        Assert.Equal(15m, payment.RefundableRemaining());
    }
}
