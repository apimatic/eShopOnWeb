using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private const string BuyerId = "shopper@example.com";
    private const int OrderId = 7;

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedCard> _cards = Substitute.For<IRepository<SavedCard>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() => new(
        _orders, _payments, _items, _cards, _gateway, _uriComposer, _logger,
        Options.Create(new PaymentSettings { Currency = "USD" }));

    private Order SetupOrder(OrderStatus status = OrderStatus.AwaitingPayment)
    {
        var order = new Order(BuyerId, new Address("s", "c", "st", "ct", "z"),
            new List<OrderItem> { new OrderItem(new CatalogItemOrdered(1, "item", "pic"), 29.00m, 1) });
        if (status == OrderStatus.PaymentAuthorized) order.MarkPaymentAuthorized();
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private Payment SetupAuthorizedPayment(Order order)
    {
        var payment = new Payment(OrderId, BuyerId, order.Total(), "USD");
        payment.MarkAuthorized("PP-ORDER-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(29));
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);
        return payment;
    }

    private static CardDetails TestCard() =>
        new("4111111111111111", "2028-12", "123", "Demo User", null);

    [Fact]
    public async Task PayAuthorizesOrderTotalAndMarksOrder()
    {
        var order = SetupOrder();
        _gateway.AuthorizeWithCardAsync(Arg.Any<CardDetails>(), 29.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationResult("PP-ORDER-1", "COMPLETED", "AUTH-1", "CREATED", 29.00m, "USD", DateTimeOffset.UtcNow.AddDays(29)));
        _payments.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Payment>());

        var service = CreateService();
        var payment = await service.PayOrderAsync(BuyerId, OrderId, TestCard(), null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public async Task PayReplayDoesNotAuthorizeTwice()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        SetupAuthorizedPayment(order);

        var service = CreateService();
        var payment = await service.PayOrderAsync(BuyerId, OrderId, TestCard(), null);

        Assert.Equal("AUTH-1", payment.AuthorizationId);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<CardDetails>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayRejectsOtherShoppersOrder()
    {
        SetupOrder();
        var service = CreateService();

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => service.PayOrderAsync("someone-else@example.com", OrderId, TestCard(), null));
    }

    [Fact]
    public async Task FulfilCapturesAndRecordsPayPalFeeAndNet()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        SetupAuthorizedPayment(order);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("AUTH-1", "CREATED", 29.00m, "USD", DateTimeOffset.UtcNow.AddDays(29)));
        _gateway.CaptureAuthorizationAsync("AUTH-1", 29.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCaptureResult("CAP-1", "COMPLETED", 29.00m, "USD", 1.24m, 27.76m));

        var service = CreateService();
        var payment = await service.FulfilOrderAsync(OrderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(29.00m, payment.CapturedAmount);
        Assert.Equal(1.24m, payment.PayPalFee);
        Assert.Equal(27.76m, payment.NetAmount);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationThenCaptures()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        SetupAuthorizedPayment(order);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("AUTH-1", "EXPIRED", 29.00m, "USD", DateTimeOffset.UtcNow.AddDays(-1)));
        _gateway.ReauthorizeAsync("AUTH-1", 29.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("AUTH-2", "CREATED", 29.00m, "USD", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAuthorizationAsync("AUTH-2", 29.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCaptureResult("CAP-2", "COMPLETED", 29.00m, "USD", 1.24m, 27.76m));

        var service = CreateService();
        var payment = await service.FulfilOrderAsync(OrderId);

        Assert.Equal("AUTH-2", payment.AuthorizationId);
        Assert.Equal("CAP-2", payment.CaptureId);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task FulfilReportsActionableErrorWhenAuthorizationCannotBeRenewed()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        SetupAuthorizedPayment(order);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("AUTH-1", "EXPIRED", 29.00m, "USD", DateTimeOffset.UtcNow.AddDays(-1)));
        _gateway.ReauthorizeAsync("AUTH-1", 29.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PayPalApiException(422, "INVALID_RESOURCE_ID", "Authorization expired.", null));

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<PaymentException>(() => service.FulfilOrderAsync(OrderId));

        Assert.Contains("could not be renewed", ex.Message);
        Assert.Contains("/pay", ex.Message);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public async Task CancelVoidsTheHold()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        var payment = SetupAuthorizedPayment(order);

        var service = CreateService();
        var result = await service.CancelOrderAsync(OrderId);

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        await _gateway.Received().VoidAuthorizationAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Same(payment, result);
    }

    [Fact]
    public async Task RefundReplayUnderSameKeyDoesNotRefundTwice()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        var payment = SetupAuthorizedPayment(order);
        payment.MarkCaptured("CAP-1", "COMPLETED", 29.00m, 1.24m, 27.76m);
        order.MarkFulfilled();
        _gateway.RefundCaptureAsync("CAP-1", 10.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-1", "COMPLETED", 10.00m, "USD"));

        var service = CreateService();
        var first = await service.RefundOrderAsync(BuyerId, OrderId, 10.00m, "key-1", null);
        var second = await service.RefundOrderAsync(BuyerId, OrderId, 10.00m, "key-1", null);

        Assert.Same(first, second);
        Assert.Equal(10.00m, payment.TotalRefunded);
        await _gateway.Received(1).RefundCaptureAsync("CAP-1", 10.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundNeverExceedsCapturedAmount()
    {
        var order = SetupOrder(OrderStatus.PaymentAuthorized);
        var payment = SetupAuthorizedPayment(order);
        payment.MarkCaptured("CAP-1", "COMPLETED", 29.00m, 1.24m, 27.76m);
        order.MarkFulfilled();
        _gateway.RefundCaptureAsync("CAP-1", 20.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-1", "COMPLETED", 20.00m, "USD"));
        _gateway.RefundCaptureAsync("CAP-1", 9.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-2", "COMPLETED", 9.00m, "USD"));

        var service = CreateService();
        await service.RefundOrderAsync(BuyerId, OrderId, 20.00m, "key-1", null);

        // 10.00 exceeds the remaining 9.00 refundable balance.
        await Assert.ThrowsAsync<PaymentException>(
            () => service.RefundOrderAsync(BuyerId, OrderId, 10.00m, "key-2", null));

        await service.RefundOrderAsync(BuyerId, OrderId, 9.00m, "key-2", null);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);

        // Fully refunded: nothing remains refundable.
        await Assert.ThrowsAsync<OrderStateException>(
            () => service.RefundOrderAsync(BuyerId, OrderId, 1.00m, "key-3", null));
    }

    [Fact]
    public async Task PayWithSavedCardUsesVaultTokenOfOwner()
    {
        var order = SetupOrder();
        var savedCard = new SavedCard(BuyerId, "eshop-abc", "VAULT-1", "VISA", "1111", "2028-12", "Demo User");
        _cards.FirstOrDefaultAsync(Arg.Any<SavedCardByIdSpecification>(), Arg.Any<CancellationToken>()).Returns(savedCard);
        _gateway.AuthorizeWithVaultedCardAsync("VAULT-1", 29.00m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationResult("PP-ORDER-2", "COMPLETED", "AUTH-9", "CREATED", 29.00m, "USD", null));
        _payments.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Payment>());

        var service = CreateService();
        var payment = await service.PayOrderAsync(BuyerId, OrderId, null, 3);

        Assert.Equal("AUTH-9", payment.AuthorizationId);
    }

    [Fact]
    public async Task PayWithAnotherShoppersSavedCardFails()
    {
        SetupOrder();
        var savedCard = new SavedCard("other@example.com", "eshop-xyz", "VAULT-9", "VISA", "1111", "2028-12", "Other");
        _cards.FirstOrDefaultAsync(Arg.Any<SavedCardByIdSpecification>(), Arg.Any<CancellationToken>()).Returns(savedCard);

        var service = CreateService();
        await Assert.ThrowsAsync<SavedCardNotFoundException>(
            () => service.PayOrderAsync(BuyerId, OrderId, null, 3));
    }
}
