using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentOrderServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IReadRepository<CatalogItem> _catalog = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IReadRepository<SavedCard> _savedCards = Substitute.For<IReadRepository<SavedCard>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IAppLogger<PaymentOrderService> _logger = Substitute.For<IAppLogger<PaymentOrderService>>();
    private readonly PayPalSettings _settings = new() { ClientId = "id", ClientSecret = "secret", Currency = "USD" };

    private PaymentOrderService CreateService() =>
        new(_orders, _catalog, _savedCards, _uriComposer, _gateway, _settings, _logger);

    private static Order MakeOrder(string buyerId, decimal total)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://pic"), total, 1);
        return new Order(buyerId, new Address("1 St", "City", "ST", "US", "00000"),
            new List<OrderItem> { item });
    }

    private void StubOrder(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    [Fact]
    public async Task Fulfil_RenewsAStaleAuthorizationBeforeCapturing()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH-OLD", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-5), "USD");
        StubOrder(order);

        _gateway.ReauthorizeAsync("AUTH-OLD", 29m, "USD", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizeResult("AUTH-NEW", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-NEW", Arg.Any<string>(), 29m, "USD", Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 29m, 1.24m, 27.76m));

        var result = await CreateService().FulfilAsync(1, CancellationToken.None);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-OLD", 29m, "USD", Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH-NEW", Arg.Any<string>(), 29m, "USD", Arg.Any<CancellationToken>());
        Assert.Equal(OrderPaymentStatus.Fulfilled, result.PaymentStatus);
        Assert.Equal("CAP-1", result.CaptureId);
    }

    [Fact]
    public async Task Fulfil_RenewsWhenCaptureReportsExpiredThenRetriesOnce()
    {
        var order = MakeOrder("alice", 15m);
        order.RecordAuthorization("PPO", "AUTH-OLD", "CREATED", DateTimeOffset.UtcNow.AddDays(2), "USD"); // not stale by clock
        StubOrder(order);

        _gateway.CaptureAsync("AUTH-OLD", Arg.Any<string>(), 15m, "USD", Arg.Any<CancellationToken>())
            .Returns<CaptureResult>(_ => throw new PayPalException("rejected", issue: "AUTHORIZATION_EXPIRED"));
        _gateway.ReauthorizeAsync("AUTH-OLD", 15m, "USD", Arg.Any<CancellationToken>())
            .Returns(new ReauthorizeResult("AUTH-NEW", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-NEW", Arg.Any<string>(), 15m, "USD", Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-9", "COMPLETED", 15m, null, null));

        var result = await CreateService().FulfilAsync(1, CancellationToken.None);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-OLD", 15m, "USD", Arg.Any<CancellationToken>());
        Assert.Equal("CAP-9", result.CaptureId);
    }

    [Fact]
    public async Task Fulfil_WhenStaleAuthorizationCannotBeRenewed_SurfacesOperatorActionableConflict()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH-OLD", "CREATED", DateTimeOffset.UtcNow.AddMinutes(-5), "USD");
        StubOrder(order);

        _gateway.ReauthorizeAsync("AUTH-OLD", 29m, "USD", Arg.Any<CancellationToken>())
            .Returns<ReauthorizeResult>(_ => throw new PayPalException("too old", issue: "AUTHORIZATION_EXPIRED"));

        var ex = await Assert.ThrowsAsync<PaymentConflictException>(
            () => CreateService().FulfilAsync(1, CancellationToken.None));
        Assert.Contains("can no longer be renewed", ex.Message);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_IsIdempotentUnderTheSameKey()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, null, null);
        StubOrder(order);

        _gateway.RefundAsync("CAP-1", Arg.Any<string>(), 10m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R1", "COMPLETED", 10m));

        var svc = CreateService();
        var (_, first) = await svc.RefundAsync("alice", 1, 10m, "key-1", CancellationToken.None);
        var (_, second) = await svc.RefundAsync("alice", 1, 10m, "key-1", CancellationToken.None);

        Assert.Equal("R1", first.PayPalRefundId);
        Assert.Same(first, second); // same recorded refund, no second gateway call
        await _gateway.Received(1).RefundAsync("CAP-1", Arg.Any<string>(), Arg.Any<decimal?>(), "USD",
            "key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_ExceedingRefundableRemaining_IsRejectedWithoutCallingPayPal()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, null, null);
        order.AddRefund(new OrderRefund("R1", 20m, "COMPLETED", "k1"));
        StubOrder(order);

        await Assert.ThrowsAsync<PaymentValidationException>(
            () => CreateService().RefundAsync("alice", 1, 15m, "k2", CancellationToken.None));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OnAnotherShoppersOrder_IsNotFound()
    {
        var order = MakeOrder("alice", 29m);
        StubOrder(order);

        var card = new CardDetails("4111111111111111", "2030-12", "123", "Bob", null, null, null, null, null, "US");
        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => CreateService().AuthorizeAsync("bob", 1, card, null, CancellationToken.None));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AFulfilledOrder_IsAConflict()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 29m, null, null);
        StubOrder(order);

        await Assert.ThrowsAsync<PaymentConflictException>(
            () => CreateService().CancelAsync(1, CancellationToken.None));
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_IsIdempotentWhenAlreadyAuthorized()
    {
        var order = MakeOrder("alice", 29m);
        order.RecordAuthorization("PPO", "AUTH", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        StubOrder(order);

        var card = new CardDetails("4111111111111111", "2030-12", "123", "Alice", null, null, null, null, null, "US");
        var result = await CreateService().AuthorizeAsync("alice", 1, card, null, CancellationToken.None);

        Assert.Equal("AUTH", result.AuthorizationId);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails?>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
