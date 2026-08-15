using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;
using CatalogItemEntity = Microsoft.eShopWeb.ApplicationCore.Entities.CatalogItem;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItemEntity> _itemRepo = Substitute.For<IRepository<CatalogItemEntity>>();
    private readonly IRepository<SavedPaymentMethod> _savedCardRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private const string Buyer = "buyer1";

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _paymentRepo, _itemRepo, _savedCardRepo, _gateway, _uriComposer,
            new PayPalSettings { Currency = "USD" }, _logger);

    private static Order OrderFor(string buyerId, decimal unitPrice = 47.5m, OrderStatus status = OrderStatus.AwaitingPayment)
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic"), unitPrice, 1) };
        var order = new Order(buyerId, new Address("s", "c", "st", "US", "z"), items);
        if (status == OrderStatus.Authorized) order.MarkAuthorized();
        return order;
    }

    private static CardPaymentDetails Card() => new("4111111111111111", "01", "2030", "123", "Name", null);

    private void GivenOwnedOrder(Order order) =>
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    private void GivenExistingPayment(Payment? payment) =>
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Payment>>(), Arg.Any<CancellationToken>()).Returns(payment);

    [Fact]
    public async Task AuthorizeIsIdempotentWhenAHoldAlreadyExists()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var existing = new Payment(1, Buyer, 47.5m, "USD", "PP");
        existing.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        GivenOwnedOrder(order);
        GivenExistingPayment(existing);

        var result = await CreateService().AuthorizeAsync(Buyer, 1, PaymentInstrument.FromCard(Card()));

        Assert.Same(existing, result);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<PaymentAmount>(), Arg.Any<string>(), Arg.Any<CardPaymentDetails>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeHappyPathPlacesHoldAndMarksOrderAuthorized()
    {
        var order = OrderFor(Buyer);
        GivenOwnedOrder(order);
        GivenExistingPayment(null);
        _gateway.AuthorizeWithCardAsync(Arg.Any<PaymentAmount>(), Arg.Any<string>(), Arg.Any<CardPaymentDetails>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizeResult("PP-ORDER", new PayPalAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3))));
        _paymentRepo.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Payment>());

        var payment = await CreateService().AuthorizeAsync(Buyer, 1, PaymentInstrument.FromCard(Card()));

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal(47.5m, payment.Amount);
        Assert.Equal(OrderStatus.Authorized, order.Status);
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeRejectsAnotherShoppersOrder()
    {
        GivenOwnedOrder(OrderFor("someoneElse"));

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            CreateService().AuthorizeAsync(Buyer, 1, PaymentInstrument.FromCard(Card())));
    }

    [Fact]
    public async Task AuthorizeWithSavedCardUsesTheVaultedCardOfTheOwner()
    {
        var order = OrderFor(Buyer);
        GivenOwnedOrder(order);
        GivenExistingPayment(null);
        var saved = new SavedPaymentMethod(Buyer, "VAULT-9", "VISA", "1111", "01", "2030", null);
        _savedCardRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>()).Returns(saved);
        _gateway.AuthorizeWithVaultedCardAsync(Arg.Any<PaymentAmount>(), Arg.Any<string>(), "VAULT-9", Arg.Any<CancellationToken>())
            .Returns(new AuthorizeResult("PP-ORDER", new PayPalAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3))));
        _paymentRepo.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Payment>());

        var payment = await CreateService().AuthorizeAsync(Buyer, 1, PaymentInstrument.FromSavedCard(1));

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        await _gateway.Received(1).AuthorizeWithVaultedCardAsync(Arg.Any<PaymentAmount>(), Arg.Any<string>(), "VAULT-9", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilCapturesAFreshHoldWithoutReauthorizing()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var payment = new Payment(1, Buyer, 47.5m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        GivenExistingPayment(payment);
        _gateway.CaptureAsync("AUTH-1", Arg.Any<PaymentAmount>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCapture("CAP-1", "COMPLETED", 47.5m, 1.72m, 45.78m, "USD"));

        var result = await CreateService().FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal(1.72m, result.PayPalFee);
        Assert.Equal(45.78m, result.NetAmount);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        await _gateway.DidNotReceive().ReauthorizeAsync(Arg.Any<string>(), Arg.Any<PaymentAmount>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilRenewsAStaleHoldBeforeCapturing()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var payment = new Payment(1, Buyer, 47.5m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1)); // stale
        _orderRepo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(order);
        GivenExistingPayment(payment);
        _gateway.ReauthorizeAsync("AUTH-1", Arg.Any<PaymentAmount>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorization("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-2", Arg.Any<PaymentAmount>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCapture("CAP-1", "COMPLETED", 47.5m, 1.72m, 45.78m, "USD"));

        var result = await CreateService().FulfilAsync(1);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", Arg.Any<PaymentAmount>(), Arg.Any<CancellationToken>());
        Assert.Equal("AUTH-2", result.AuthorizationId);
        Assert.Equal(PaymentStatus.Captured, result.Status);
    }

    [Fact]
    public async Task RefundReplayUnderTheSameKeyDoesNotRefundTwice()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var payment = new Payment(1, Buyer, 100m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP-1", "COMPLETED", 100m, 3m, 97m);
        payment.AddRefund("REF-1", 10m, "COMPLETED", "key-1");
        GivenOwnedOrder(order);
        GivenExistingPayment(payment);

        var refund = await CreateService().RefundAsync(Buyer, 1, 10m, "key-1");

        Assert.Equal("REF-1", refund.PayPalRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<PaymentAmount>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundCannotExceedTheCapturedAmount()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var payment = new Payment(1, Buyer, 100m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP-1", "COMPLETED", 100m, 3m, 97m);
        GivenOwnedOrder(order);
        GivenExistingPayment(payment);

        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            CreateService().RefundAsync(Buyer, 1, 150m, "key-1"));
    }

    [Fact]
    public async Task RefundIssuesAPartialRefundAndRecordsIt()
    {
        var order = OrderFor(Buyer, status: OrderStatus.Authorized);
        var payment = new Payment(1, Buyer, 100m, "USD", "PP");
        payment.SetAuthorized("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        payment.SetCaptured("CAP-1", "COMPLETED", 100m, 3m, 97m);
        GivenOwnedOrder(order);
        GivenExistingPayment(payment);
        _gateway.RefundAsync("CAP-1", Arg.Any<PaymentAmount>(), "key-1", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefund("REF-1", "COMPLETED", 25m, "USD"));

        var refund = await CreateService().RefundAsync(Buyer, 1, 25m, "key-1");

        Assert.Equal("REF-1", refund.PayPalRefundId);
        Assert.Equal(25m, refund.Amount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(75m, payment.RefundableAmount);
    }
}
