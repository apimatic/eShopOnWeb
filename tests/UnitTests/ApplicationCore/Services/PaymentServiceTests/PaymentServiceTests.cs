using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PaymentServiceTests
{
    private const string Buyer = "buyer-1";
    private static readonly CancellationToken CT = CancellationToken.None;

    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _methods = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        var uriComposer = Substitute.For<IUriComposer>();
        var options = Options.Create(new PayPalSettings { Currency = "USD" });
        _service = new PaymentService(_orders, _items, _methods, _gateway, uriComposer, options);
    }

    private static CardDetails Card() =>
        new("4111111111111111", "2030-01", "123", "Test Shopper",
            new BillingAddress(null, null, null, null, null, "US"));

    private static Order AwaitingOrder(string buyer = Buyer, decimal total = 100m)
    {
        var address = new Address("street", "city", "state", "US", "00000");
        var item = new OrderItem(new CatalogItemOrdered(1, "product", "uri"), total, 1);
        return new Order(buyer, address, new List<OrderItem> { item });
    }

    private static Order AuthorizedOrder(string buyer = Buyer, decimal total = 100m)
    {
        var order = AwaitingOrder(buyer, total);
        order.RecordAuthorization(new OrderPayment("PPO-1", "AUTH-1", "CREATED", null, "USD", total, "Visa ****1111", false));
        return order;
    }

    private static Order FulfilledOrder(string buyer = Buyer, decimal total = 100m, decimal fee = 3m)
    {
        var order = AuthorizedOrder(buyer, total);
        order.Payment!.RecordCapture("CAP-1", "COMPLETED", total, fee, total - fee);
        order.RecordFulfilment();
        return order;
    }

    private void OrderLookupReturns(Order? order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    [Fact]
    public async Task Pay_WithCard_PlacesHoldAndMarksAuthorized()
    {
        var order = AwaitingOrder(total: 50m);
        OrderLookupReturns(order);
        _gateway.AuthorizeWithCardAsync(50m, "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH", "CREATED", null));

        await _service.PayOrderAsync(Buyer, 1, new PaymentInstrument(Card(), null), CT);

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal("AUTH", order.Payment!.AuthorizationId);
        await _gateway.Received(1).AuthorizeWithCardAsync(50m, "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orders.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_WithSavedCard_UsesVaultedToken()
    {
        var order = AwaitingOrder(total: 75m);
        OrderLookupReturns(order);
        _methods.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns(new SavedPaymentMethod(Buyer, "VAULT-9", "Visa", "1111", "2030-01"));
        _gateway.AuthorizeWithVaultedCardAsync(75m, "USD", "VAULT-9", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH", "CREATED", null));

        await _service.PayOrderAsync(Buyer, 1, new PaymentInstrument(null, 9), CT);

        Assert.True(order.Payment!.UsedSavedCard);
        await _gateway.Received(1).AuthorizeWithVaultedCardAsync(75m, "USD", "VAULT-9", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_AlreadyAuthorized_IsIdempotentNoOp()
    {
        OrderLookupReturns(AuthorizedOrder());

        await _service.PayOrderAsync(Buyer, 1, new PaymentInstrument(Card(), null), CT);

        await _gateway.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_ForAnotherShoppersOrder_IsNotFoundAndNeverCallsGateway()
    {
        OrderLookupReturns(AwaitingOrder(buyer: "someone-else"));

        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => _service.PayOrderAsync(Buyer, 1, new PaymentInstrument(Card(), null), CT));

        await _gateway.DidNotReceive().AuthorizeWithCardAsync(
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var order = AuthorizedOrder(total: 100m);
        OrderLookupReturns(order);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationState("CREATED", DateTimeOffset.Now.AddDays(2)));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 100m, 3.20m, 96.80m, "USD"));

        await _service.FulfilOrderAsync(1, CT);

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(3.20m, order.Payment.PayPalFee);
        Assert.Equal(96.80m, order.Payment.NetAmount);
        await _gateway.DidNotReceive().ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenHoldIsStale_RenewsThenCaptures()
    {
        var order = AuthorizedOrder(total: 100m);
        OrderLookupReturns(order);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationState("CREATED", DateTimeOffset.Now.AddDays(-1))); // stale
        _gateway.ReauthorizeAsync("AUTH-1", 100m, "USD", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizationResult("AUTH-2", "CREATED", DateTimeOffset.Now.AddDays(3)));
        _gateway.CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-2", "COMPLETED", 100m, 3m, 97m, "USD"));

        await _service.FulfilOrderAsync(1, CT);

        Assert.Equal("AUTH-2", order.Payment!.AuthorizationId);
        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", 100m, "USD", Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsHoldAndMarksCancelled()
    {
        var order = AuthorizedOrder();
        OrderLookupReturns(order);

        await _service.CancelOrderAsync(1, CT);

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        await _gateway.Received(1).VoidAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_Full_RefundsRemainingAndMarksRefunded()
    {
        var order = FulfilledOrder(total: 100m);
        OrderLookupReturns(order);
        _gateway.RefundAsync("CAP-1", null, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF-1", "COMPLETED", 100m, "USD"));

        await _service.RefundOrderAsync(Buyer, 1, null, "key-1", CT);

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        await _gateway.Received(1).RefundAsync("CAP-1", null, "USD", "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedUnderSameKey_DoesNotRefundTwice()
    {
        var order = FulfilledOrder(total: 100m);
        order.RecordRefund(new PaymentRefund("REF-EXISTING", "COMPLETED", 40m, "USD", "key-1"));
        OrderLookupReturns(order);

        await _service.RefundOrderAsync(Buyer, 1, 40m, "key-1", CT);

        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
    }

    [Fact]
    public async Task Refund_BeyondRemainingCaptured_IsRejected()
    {
        var order = FulfilledOrder(total: 100m);
        order.RecordRefund(new PaymentRefund("REF-EXISTING", "COMPLETED", 40m, "USD", "key-1"));
        OrderLookupReturns(order);

        await Assert.ThrowsAsync<PaymentException>(() => _service.RefundOrderAsync(Buyer, 1, 70m, "key-2", CT));

        await _gateway.DidNotReceive().RefundAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletePaymentMethod_RemovesFromVaultAndStore()
    {
        var saved = new SavedPaymentMethod(Buyer, "VAULT-9", "Visa", "1111", "2030-01");
        _methods.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>()).Returns(saved);

        await _service.DeletePaymentMethodAsync(Buyer, 5, CT);

        await _gateway.Received(1).DeleteVaultedCardAsync("VAULT-9", Arg.Any<CancellationToken>());
        await _methods.Received(1).DeleteAsync(saved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeletePaymentMethod_NotOwned_IsNotFoundAndNeverCallsGateway()
    {
        _methods.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _service.DeletePaymentMethodAsync(Buyer, 5, CT));

        await _gateway.DidNotReceive().DeleteVaultedCardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
