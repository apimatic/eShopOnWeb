using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _methods = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IPaymentConfiguration _config = Substitute.For<IPaymentConfiguration>();

    public OrderPaymentServiceTests() => _config.Currency.Returns("USD");

    private OrderPaymentService Service() => new(_orders, _items, _methods, _gateway, _uri, _config);

    private static Order MakeOrder(string buyer, Action<Payment>? advance = null)
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "Widget", "uri"), 50m, 2) }; // total 100
        var order = new Order(buyer, new Address("s", "c", "st", "country", "zip"), items);
        order.StartPayment("USD");
        advance?.Invoke(order.Payment!);
        return order;
    }

    private void RepoReturns(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    private static PayOrderInstruction RawCard() =>
        new() { Card = new PayPalCardDetails("4111111111111111", "2027-12", "123", "Name") };

    [Fact]
    public async Task PayAsync_authorizes_a_pending_order()
    {
        var order = MakeOrder("demo");
        RepoReturns(order);
        _gateway.AuthorizeAsync(Arg.Any<PayPalPaymentInstrument>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29)));

        await Service().PayAsync(1, "demo", RawCard());

        Assert.Equal(PaymentStatus.Authorized, order.Payment!.Status);
        Assert.Equal("AUTH", order.Payment.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<PayPalPaymentInstrument>(), 100m, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orders.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAsync_is_idempotent_when_already_authorized()
    {
        var order = MakeOrder("demo", p => p.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29)));
        RepoReturns(order);

        await Service().PayAsync(1, "demo", RawCard());

        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalPaymentInstrument>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAsync_hides_another_shoppers_order()
    {
        var order = MakeOrder("owner");
        RepoReturns(order);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => Service().PayAsync(1, "intruder", RawCard()));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalPaymentInstrument>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_is_idempotent_on_the_callers_key()
    {
        var order = MakeOrder("demo", p =>
        {
            p.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29));
            p.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m);
        });
        RepoReturns(order);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("RF", "COMPLETED", 10m));

        var first = await Service().RefundAsync(1, "demo", "same-key", 10m);
        var second = await Service().RefundAsync(1, "demo", "same-key", 10m);

        Assert.Same(first, second);
        Assert.Equal(10m, order.Payment!.TotalRefunded); // not 20 — never refunded twice
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAsync_rejects_amount_beyond_captured()
    {
        var order = MakeOrder("demo", p =>
        {
            p.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29));
            p.MarkCaptured("CAP", "COMPLETED", 100m, 3m, 97m);
        });
        RepoReturns(order);

        await Assert.ThrowsAsync<PaymentOperationException>(() => Service().RefundAsync(1, "demo", "k", 150m));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilAsync_renews_a_stale_authorization_then_captures()
    {
        var order = MakeOrder("demo", p => p.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(-1))); // expired
        RepoReturns(order);
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalAuthorizationResult(null, "AUTH2", "CREATED", DateTimeOffset.Now.AddDays(29)));
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP", "COMPLETED", 100m, 3m, 97m));

        await Service().FulfilAsync(1);

        Assert.Equal(PaymentStatus.Captured, order.Payment!.Status);
        Assert.Equal("AUTH2", order.Payment.AuthorizationId);
        await _gateway.Received(1).ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelAsync_voids_an_authorized_order()
    {
        var order = MakeOrder("demo", p => p.MarkAuthorized("PPO", "AUTH", "CREATED", DateTimeOffset.Now.AddDays(29)));
        RepoReturns(order);

        await Service().CancelAsync(1);

        Assert.Equal(PaymentStatus.Voided, order.Payment!.Status);
        await _gateway.Received(1).VoidAsync("AUTH", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
