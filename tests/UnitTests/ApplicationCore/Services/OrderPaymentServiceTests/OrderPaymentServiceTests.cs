using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string Buyer = "buyer@test.com";
    private const string Currency = "USD";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _cards = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPayPalPaymentService _payPal = Substitute.For<IPayPalPaymentService>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService NewService() =>
        new(_orders, _items, _cards, _payPal, _uri, new PayPalSettings { Currency = Currency }, _logger);

    private static Order AwaitingOrder(string buyer = Buyer, decimal amount = 29m)
    {
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Test item", "pic.png"), amount, 1)
        };
        return new Order(buyer, new Address("s", "c", "st", "co", "00000"), items);
    }

    private static Order AuthorizedOrder(string buyer = Buyer, decimal amount = 29m,
        DateTimeOffset? expires = null, string authId = "AUTH1")
    {
        var order = AwaitingOrder(buyer, amount);
        var payment = new OrderPayment("PPO1", authId, "CREATED", amount, Currency,
            expires ?? DateTimeOffset.UtcNow.AddDays(20), "ref-1", null);
        order.MarkAuthorized(payment);
        return order;
    }

    private static Order FulfilledOrder(string buyer = Buyer, decimal amount = 29m)
    {
        var order = AuthorizedOrder(buyer, amount);
        order.Payment!.RecordCapture("CAP1", "COMPLETED", amount, 1.24m, amount - 1.24m);
        order.MarkFulfilled();
        return order;
    }

    private void ReturnsOrder(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentSpecification>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task Authorize_WhenAwaitingPayment_HoldsAndMarksAuthorized()
    {
        var order = AwaitingOrder();
        ReturnsOrder(order);
        _payPal.AuthorizeAsync(Arg.Any<AuthorizeCardPaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH", "CREATED", DateTimeOffset.UtcNow.AddDays(20)));

        var result = await NewService().AuthorizeAsync(1, Buyer,
            new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, "US"),
            null, CancellationToken.None);

        Assert.Equal(OrderStatus.Authorized, result.Status);
        Assert.NotNull(result.Payment);
        await _orders.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotent()
    {
        ReturnsOrder(AuthorizedOrder());

        await NewService().AuthorizeAsync(1, Buyer,
            new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, "US"),
            null, CancellationToken.None);

        // A double-click must not authorize a second time.
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCardPaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_ForAnotherBuyer_ThrowsNotFound()
    {
        ReturnsOrder(AuthorizedOrder(buyer: "someone-else@test.com"));

        await Assert.ThrowsAsync<EntityNotFoundException>(() => NewService().AuthorizeAsync(1, Buyer,
            new CardDetails("4111111111111111", "2027-12", "123", null, null, null, null, null, null, "US"),
            null, CancellationToken.None));
    }

    [Fact]
    public async Task Authorize_WithUnknownSavedCard_ThrowsNotFound()
    {
        ReturnsOrder(AwaitingOrder());
        _cards.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodsByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            NewService().AuthorizeAsync(1, Buyer, null, 999, CancellationToken.None));
    }

    [Fact]
    public async Task Fulfil_WhenAuthorized_CapturesAndRecordsFeeAndNet()
    {
        var order = AuthorizedOrder();
        ReturnsOrder(order);
        _payPal.CaptureAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 29m, Currency, 1.24m, 27.76m));

        var result = await NewService().FulfilAsync(1, CancellationToken.None);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal(29m, result.Payment!.CapturedAmount);
        Assert.Equal(1.24m, result.Payment.PayPalFee);
        Assert.Equal(27.76m, result.Payment.NetAmount);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationStale_RenewsBeforeCapture()
    {
        // Hold already expired ⇒ proactively re-authorize, then capture on the NEW authorization.
        var order = AuthorizedOrder(expires: DateTimeOffset.UtcNow.AddMinutes(-5), authId: "AUTH1");
        ReturnsOrder(order);
        _payPal.ReauthorizeAsync("AUTH1", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH2", "CREATED", DateTimeOffset.UtcNow.AddDays(29)));
        _payPal.CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 29m, Currency, 1.24m, 27.76m));

        var result = await NewService().FulfilAsync(1, CancellationToken.None);

        await _payPal.Received(1).ReauthorizeAsync("AUTH1", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payPal.Received(1).CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.Fulfilled, result.Status);
    }

    [Fact]
    public async Task Fulfil_WhenCaptureReportsExpired_RenewsThenCaptures()
    {
        // Hold looks fresh, but the capture reports it expired ⇒ renew reactively, then capture.
        var order = AuthorizedOrder(expires: DateTimeOffset.UtcNow.AddDays(20), authId: "AUTH1");
        ReturnsOrder(order);
        _payPal.CaptureAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CaptureResult>(_ => throw new PaymentAuthorizationExpiredException("AUTHORIZATION_EXPIRED"));
        _payPal.ReauthorizeAsync("AUTH1", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("", "AUTH2", "CREATED", DateTimeOffset.UtcNow.AddDays(29)));
        _payPal.CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP1", "COMPLETED", 29m, Currency, 1.24m, 27.76m));

        var result = await NewService().FulfilAsync(1, CancellationToken.None);

        await _payPal.Received(1).ReauthorizeAsync("AUTH1", Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.Fulfilled, result.Status);
    }

    [Fact]
    public async Task Fulfil_WhenRenewalRejected_ThrowsPaymentRejected()
    {
        var order = AuthorizedOrder(expires: DateTimeOffset.UtcNow.AddMinutes(-5));
        ReturnsOrder(order);
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Currency, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<AuthorizationResult>(_ => throw new PaymentRejectedException("beyond honor period"));

        await Assert.ThrowsAsync<PaymentRejectedException>(() => NewService().FulfilAsync(1, CancellationToken.None));
        await _payPal.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAlreadyFulfilled_IsIdempotent()
    {
        ReturnsOrder(FulfilledOrder());

        await NewService().FulfilAsync(1, CancellationToken.None);

        await _payPal.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_WhenAuthorized_VoidsAndMarksCancelled()
    {
        var order = AuthorizedOrder();
        ReturnsOrder(order);

        var result = await NewService().CancelAsync(1, CancellationToken.None);

        await _payPal.Received(1).VoidAsync("AUTH1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Cancel_WhenFulfilled_ThrowsInvalidState()
    {
        ReturnsOrder(FulfilledOrder());

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => NewService().CancelAsync(1, CancellationToken.None));
        await _payPal.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_SameKeyTwice_RefundsOnce()
    {
        var order = FulfilledOrder();
        ReturnsOrder(order);
        _payPal.GetCapturedAmountAsync("CAP1", Arg.Any<CancellationToken>()).Returns(29m);
        _payPal.RefundAsync("CAP1", 10m, Currency, "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R1", "COMPLETED", 10m, Currency));

        var first = await NewService().RefundAsync(1, Buyer, 10m, "key-1", CancellationToken.None);
        var second = await NewService().RefundAsync(1, Buyer, 10m, "key-1", CancellationToken.None);

        Assert.Equal("R1", first.PayPalRefundId);
        Assert.Equal(first.PayPalRefundId, second.PayPalRefundId);
        // Idempotent: PayPal is called exactly once even though the request repeats.
        await _payPal.Received(1).RefundAsync("CAP1", 10m, Currency, "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctKeys_RefundTwice()
    {
        var order = FulfilledOrder();
        ReturnsOrder(order);
        _payPal.GetCapturedAmountAsync("CAP1", Arg.Any<CancellationToken>()).Returns(29m);
        _payPal.RefundAsync("CAP1", 10m, Currency, "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R1", "COMPLETED", 10m, Currency));
        _payPal.RefundAsync("CAP1", 5m, Currency, "key-2", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R2", "COMPLETED", 5m, Currency));

        await NewService().RefundAsync(1, Buyer, 10m, "key-1", CancellationToken.None);
        await NewService().RefundAsync(1, Buyer, 5m, "key-2", CancellationToken.None);

        Assert.Equal(15m, order.Payment!.TotalRefunded());
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public async Task Refund_BeyondCaptured_ThrowsAndDoesNotCallPayPal()
    {
        var order = FulfilledOrder();
        ReturnsOrder(order);
        _payPal.GetCapturedAmountAsync("CAP1", Arg.Any<CancellationToken>()).Returns(29m);

        await Assert.ThrowsAsync<InvalidOrderStateException>(() =>
            NewService().RefundAsync(1, Buyer, 100m, "key-x", CancellationToken.None));
        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_FullyRefunded_SetsRefundedStatus()
    {
        var order = FulfilledOrder(amount: 20m);
        ReturnsOrder(order);
        _payPal.GetCapturedAmountAsync("CAP1", Arg.Any<CancellationToken>()).Returns(20m);
        _payPal.RefundAsync("CAP1", 20m, Currency, "key-full", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R1", "COMPLETED", 20m, Currency));

        await NewService().RefundAsync(1, Buyer, 20m, "key-full", CancellationToken.None);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment!.RemainingRefundable());
    }
}
