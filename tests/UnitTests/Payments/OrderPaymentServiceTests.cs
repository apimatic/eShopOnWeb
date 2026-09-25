using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

public class OrderPaymentServiceTests
{
    private const string Buyer = "shopper@example.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<SavedPaymentMethod> _cards = Substitute.For<IReadRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IIdempotencyService _idempotency = Substitute.For<IIdempotencyService>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();

    private OrderPaymentService BuildService()
    {
        _idempotency.TryClaimAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        return new OrderPaymentService(_orders, _items, _cards, _gateway, _idempotency, _uri,
            new PaymentSettings { CurrencyCode = "USD" }, Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    private static Order NewAwaitingOrder()
    {
        var address = new Address("s", "c", "st", "co", "z");
        var item = new OrderItem(new CatalogItemOrdered(1, "widget", "pic.png"), 20m, 1);
        return new Order(Buyer, address, new List<OrderItem> { item });
    }

    private static Order NewFulfilledOrder(decimal captured = 20m)
    {
        var order = NewAwaitingOrder();
        order.BeginPayment("USD", "ESHOP-REF", captured);
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null);
        order.RecordCapture("CAP-1", "COMPLETED", captured, 0.9m, captured - 0.9m);
        return order;
    }

    private void ReturnOrder(Order order) =>
        _orders.ListAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Order> { order }));

    [Fact]
    public async Task PayAsync_requires_exactly_one_funding_source()
    {
        ReturnOrder(NewAwaitingOrder());
        var service = BuildService();

        await Assert.ThrowsAsync<PaymentOperationException>(
            () => service.PayAsync(Buyer, 1, card: null, savedPaymentMethodId: null));

        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAsync_is_idempotent_when_already_authorized()
    {
        var order = NewAwaitingOrder();
        order.BeginPayment("USD", "ESHOP-REF", 20m);
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null);
        ReturnOrder(order);
        var service = BuildService();

        var view = await service.PayAsync(Buyer, 1, new CardInput("4111111111111111", "2027-01", "123", "N"), null);

        Assert.Equal("Authorized", view.PaymentStatus);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAsync_rejects_order_owned_by_another_shopper()
    {
        ReturnOrder(NewAwaitingOrder()); // owned by Buyer
        var service = BuildService();

        var ex = await Assert.ThrowsAsync<PaymentOperationException>(
            () => service.PayAsync("someone-else@example.com", 1, new CardInput("4111111111111111", "2027-01", "123", "N"), null));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task RefundAsync_rejects_over_refund()
    {
        ReturnOrder(NewFulfilledOrder(captured: 20m));
        var service = BuildService();

        var ex = await Assert.ThrowsAsync<PaymentOperationException>(
            () => service.RefundAsync(Buyer, 1, amount: 50m, idempotencyKey: "k1", note: null));

        Assert.Equal(PaymentErrorKind.Validation, ex.Kind);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_replays_same_key_without_refunding_twice()
    {
        var order = NewFulfilledOrder(captured: 20m);
        order.RecordRefund("k1", "RE-1", 5m, "COMPLETED"); // an existing refund under key k1
        ReturnOrder(order);
        var service = BuildService();

        var (_, refund) = await service.RefundAsync(Buyer, 1, amount: 5m, idempotencyKey: "k1", note: null);

        Assert.Equal("RE-1", refund.RefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilAsync_captures_and_records_fee_and_net()
    {
        var order = NewAwaitingOrder();
        order.BeginPayment("USD", "ESHOP-REF", 20m);
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null);
        ReturnOrder(order);
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-1", "COMPLETED", 20m, 0.9m, 19.1m, "USD"));
        var service = BuildService();

        var view = await service.FulfilAsync(1);

        Assert.Equal("Fulfilled", view.PaymentStatus);
        Assert.Equal("CAP-1", view.CaptureId);
        Assert.Equal(0.9m, view.PayPalFee);
        Assert.Equal(19.1m, view.NetAmount);
    }
}
