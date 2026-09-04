using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class OrderPaymentServiceTests
{
    private const string Buyer = "demouser@microsoft.com";
    private const string OtherBuyer = "other@microsoft.com";
    private static readonly Address ShipTo = new("1 Main St", "Redmond", "WA", "US", "98052");

    private readonly CatalogContext _context;
    private readonly IPaymentGateway _gateway;
    private readonly OrderPaymentService _service;

    public OrderPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"PaymentsTest-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
        _gateway = Substitute.For<IPaymentGateway>();

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns(call => call.Arg<string>());

        _service = new OrderPaymentService(
            new EfRepository<Order>(_context),
            new EfRepository<CatalogItem>(_context),
            new EfRepository<SavedPaymentMethod>(_context),
            _gateway,
            uriComposer,
            new EfUnitOfWork(_context),
            new PayPalSettings { Currency = "USD" },
            Substitute.For<IAppLogger<OrderPaymentService>>());
    }

    private int SeedCatalogItem(decimal price)
    {
        var item = new CatalogItem(1, 1, "desc", "mug", price, "/img.png");
        _context.CatalogItems.Add(item);
        _context.SaveChanges();
        return item.Id;
    }

    private async Task<Order> PlacedOrder(decimal price = 10m, int quantity = 1, string buyerId = Buyer)
    {
        var catalogItemId = SeedCatalogItem(price);
        // Fresh Address instance per order — EF change-tracks owned value instances.
        var result = await _service.PlaceOrderAsync(buyerId, new List<PlaceOrderLine> { new(catalogItemId, quantity) }, new Address("1 Main St", "Redmond", "WA", "US", "98052"));
        return result.Order;
    }

    private void GatewayAuthorizes(GatewayAuthorization? authorization = null)
    {
        _gateway.AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(authorization ?? LiveAuthorization());
        _gateway.AuthorizeExistingOrderAsync(Arg.Any<string>(), Arg.Any<GatewayAuthorizeSource>(), Arg.Any<CancellationToken>())
            .Returns(authorization ?? LiveAuthorization());
    }

    private static GatewayAuthorization LiveAuthorization(decimal amount = 10m) =>
        new("AUTH-1", AuthorizationStatuses.Created, amount, "USD", DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow, "NTR-9", "PP-ORDER-1");

    [Fact]
    public async Task PlaceOrder_creates_order_awaiting_payment_with_catalog_prices()
    {
        var order = await PlacedOrder(price: 19.5m, quantity: 2);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(39m, order.Total());
        Assert.Equal(Buyer, order.BuyerId);
        Assert.Null(order.Payment);
    }

    [Fact]
    public async Task PlaceOrder_rejects_unknown_catalog_items_and_bad_quantities()
    {
        await Assert.ThrowsAsync<ValidationFailureException>(() =>
            _service.PlaceOrderAsync(Buyer, new List<PlaceOrderLine> { new(4242, 1) }, ShipTo));
        await Assert.ThrowsAsync<ValidationFailureException>(() =>
            _service.PlaceOrderAsync(Buyer, new List<PlaceOrderLine> { new(1, 0) }, ShipTo));
    }

    [Fact]
    public async Task Pay_authorizes_exact_order_total_and_stores_provider_state()
    {
        var order = await PlacedOrder(price: 9.6m);
        GatewayAuthorizes();

        var result = await _service.PayAsync(order.Id, Buyer, new PayCommand(new CardCredential("4111111111111111", "09/2029", "123", "Test Buyer", null), null, null));

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", result.Payment.AuthorizationId);
        Assert.Equal("PP-ORDER-1", result.Payment.ProviderOrderId);
        Assert.False(result.Replayed);

        await _gateway.Received(1).AuthorizeAsync(
            Arg.Is<GatewayAuthorizeRequest>(r =>
                r.Amount == 9.6m && r.Currency == "USD" &&
                r.CustomReference == $"eshop-order-{order.Id}" &&
                r.InvoiceReference.StartsWith($"eshop-order-{order.Id}-", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_double_click_authorizes_only_once()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes();

        var first = await _service.PayAsync(order.Id, Buyer, CardPay());
        var second = await _service.PayAsync(order.Id, Buyer, CardPay());

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal("AUTH-1", second.Payment.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_other_buyers_order_is_not_found()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes();

        await Assert.ThrowsAsync<OrderNotFoundException>(() => _service.PayAsync(order.Id, OtherBuyer, CardPay()));
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_requires_exactly_one_payment_source()
    {
        var order = await PlacedOrder();

        await Assert.ThrowsAsync<ValidationFailureException>(() => _service.PayAsync(order.Id, Buyer, new PayCommand(null, null, null)));
        await Assert.ThrowsAsync<ValidationFailureException>(() => _service.PayAsync(order.Id, Buyer, new PayCommand(new CardCredential("4111111111111111", "09/2029", "123", null, null), "pm-1", null)));
    }

    [Fact]
    public async Task Pay_with_saved_card_uses_vault_token_and_reuses_network_reference()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes();
        _gateway.VaultCardAsync(Arg.Any<string>(), Arg.Any<CardCredential>(), Arg.Any<CancellationToken>())
            .Returns(new SavedVaultCard("TOKEN-1", "CUST-1", Buyer, "VISA", "1111", "09/2029", "Test Buyer"));

        var saved = await _service.SaveCardAsync(Buyer, new CardCredential("4111111111111111", "09/2029", "123", "Test Buyer", null));
        Assert.Equal("TOKEN-1", saved.VaultTokenId);
        Assert.Equal("1111", saved.Last4);
        Assert.DoesNotContain("4111111111111111", System.Text.Json.JsonSerializer.Serialize(saved));

        var result = await _service.PayAsync(order.Id, Buyer, new PayCommand(null, saved.ExternalId, null));

        Assert.Equal(OrderStatus.Authorized, result.Order.Status);
        await _gateway.Received(1).AuthorizeAsync(
            Arg.Is<GatewayAuthorizeRequest>(r => r.Source.VaultTokenId == "TOKEN-1" && r.Source.Card == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_after_delete_cannot_use_the_saved_card()
    {
        GatewayAuthorizes();
        _gateway.VaultCardAsync(Arg.Any<string>(), Arg.Any<CardCredential>(), Arg.Any<CancellationToken>())
            .Returns(new SavedVaultCard("TOKEN-1", "CUST-1", Buyer, "VISA", "1111", "09/2029", null));
        var saved = await _service.SaveCardAsync(Buyer, new CardCredential("4111111111111111", "09/2029", "123", null, null));

        await _service.DeleteCardAsync(Buyer, saved.ExternalId);

        Assert.Empty(await _service.ListCardsAsync(Buyer));
        var order = await PlacedOrder();
        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() => _service.PayAsync(order.Id, Buyer, new PayCommand(null, saved.ExternalId, null)));
        await _gateway.Received(1).DeleteVaultCardAsync("TOKEN-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_with_another_shoppers_saved_card_is_not_found()
    {
        GatewayAuthorizes();
        _gateway.VaultCardAsync(Arg.Any<string>(), Arg.Any<CardCredential>(), Arg.Any<CancellationToken>())
            .Returns(new SavedVaultCard("TOKEN-1", "CUST-1", Buyer, "VISA", "1111", "09/2029", null));
        var saved = await _service.SaveCardAsync(Buyer, new CardCredential("4111111111111111", "09/2029", "123", null, null));

        var order = await PlacedOrder(buyerId: OtherBuyer);
        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() => _service.PayAsync(order.Id, OtherBuyer, new PayCommand(null, saved.ExternalId, null)));
    }

    [Fact]
    public async Task Pay_after_unknown_outcome_recovers_the_existing_hold_instead_of_a_second_one()
    {
        var order = await PlacedOrder();
        var pending = new PaymentGatewayException(PaymentFailureKind.OutcomeUnknown, "connection dropped mid-send")
        {
            ProviderOrderId = "PP-PENDING"
        };
        _gateway.AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<GatewayAuthorization>(pending));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => _service.PayAsync(order.Id, Buyer, CardPay()));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal("PP-PENDING", order.Payment!.ProviderOrderId);
        Assert.True(order.Payment.HasPendingAuthorizationToRecover);

        // The hold actually reached PayPal: the replay recovers it from provider state.
        _gateway.GetOrderSnapshotAsync("PP-PENDING", Arg.Any<CancellationToken>())
            .Returns(new GatewayOrderSnapshot("PP-PENDING", "COMPLETED",
                new List<GatewayAuthorization> { new("AUTH-R", AuthorizationStatuses.Created, 10m, "USD", DateTimeOffset.UtcNow.AddDays(20), DateTimeOffset.UtcNow, "NTR-R", "PP-PENDING") },
                new List<GatewayCapture>(),
                new List<GatewayRefund>()));

        var second = await _service.PayAsync(order.Id, Buyer, CardPay());

        Assert.Equal(OrderStatus.Authorized, second.Order.Status);
        Assert.Equal("AUTH-R", second.Payment.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().AuthorizeExistingOrderAsync(Arg.Any<string>(), Arg.Any<GatewayAuthorizeSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_releases_the_hold_and_never_captures()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes();
        await _service.PayAsync(order.Id, Buyer, CardPay());

        var result = await _service.CancelAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Order.Status);
        Assert.True(result.FundsReleased);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_after_fulfil_is_refused()
    {
        await AuthorizeAndCapture();

        await Assert.ThrowsAsync<OrderStateException>(() => _service.CancelAsync(_fulfilledOrderId));
        await _gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_captures_the_authorized_amount_and_records_fee_and_net()
    {
        var order = await PlacedOrder(price: 9.6m);
        GatewayAuthorizes(LiveAuthorization(9.6m));
        await _service.PayAsync(order.Id, Buyer, CardPay());
        _gateway.CaptureAsync("AUTH-1", 9.6m, "USD", Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-1", CaptureStatuses.Completed, null, 9.6m, 0.56m, 9.04m, "USD", "AUTH-1", "PP-ORDER-1"));

        var result = await _service.FulfilAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        Assert.Equal(9.6m, result.Payment.CapturedAmount);
        Assert.Equal(0.56m, result.Payment.FeeAmount);
        Assert.Equal(9.04m, result.Payment.NetAmount);

        var replay = await _service.FulfilAsync(order.Id);
        Assert.True(replay.Replayed);
        await _gateway.Received(1).CaptureAsync("AUTH-1", 9.6m, "USD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_renews_an_expired_authorization_then_captures_the_renewal()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes(new GatewayAuthorization(
            "AUTH-1", AuthorizationStatuses.Created, 10m, "USD",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(-5), null, "PP-ORDER-1"));
        await _service.PayAsync(order.Id, Buyer, CardPay());

        _gateway.ReauthorizeAsync("AUTH-1", 10m, "USD", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("AUTH-2", AuthorizationStatuses.Created, 10m, "USD", DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow, null, "PP-ORDER-1"));
        _gateway.CaptureAsync("AUTH-2", 10m, "USD", Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-2", CaptureStatuses.Completed, null, 10m, 0.6m, 9.4m, "USD", "AUTH-2", "PP-ORDER-1"));

        var result = await _service.FulfilAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", 10m, "USD", Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH-2", 10m, "USD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_beyond_the_30_day_window_reports_an_actionable_refusal()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes(new GatewayAuthorization(
            "AUTH-1", AuthorizationStatuses.Created, 10m, "USD",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(-31), null, "PP-ORDER-1"));
        await _service.PayAsync(order.Id, Buyer, CardPay());

        var ex = await Assert.ThrowsAsync<OrderStateException>(() => _service.FulfilAsync(order.Id));

        Assert.Contains("30-day", ex.Message, StringComparison.OrdinalIgnoreCase);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_full_after_fulfil_then_nothing_more_refundable()
    {
        await AuthorizeAndCapture();
        _gateway.RefundAsync("CAP-1", 10m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-1", RefundStatuses.Completed, 10m, "USD", 10m, "CAP-1", null));

        var result = await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(null, "key-full"));

        Assert.Equal("REF-1", result.Refund.ProviderRefundId);
        Assert.Equal(0m, result.RemainingRefundableAmount);
        await _gateway.Received(1).RefundAsync("CAP-1", 10m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_same_idempotency_key_never_refunds_twice()
    {
        await AuthorizeAndCapture();
        _gateway.RefundAsync("CAP-1", 4m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-1", RefundStatuses.Completed, 4m, "USD", 4m, "CAP-1", null));

        var first = await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(4m, "key-partial"));
        var replay = await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(4m, "key-partial"));

        Assert.True(replay.Replayed);
        Assert.Equal(first.Refund.ProviderRefundId, replay.Refund.ProviderRefundId);
        await _gateway.Received(1).RefundAsync("CAP-1", 4m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_distinct_partial_refunds_remain_legitimate_but_capped_at_the_capture()
    {
        await AuthorizeAndCapture();
        _gateway.RefundAsync("CAP-1", 4m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-1", RefundStatuses.Completed, 4m, "USD", 4m, "CAP-1", null));
        _gateway.RefundAsync("CAP-1", 6m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-2", RefundStatuses.Completed, 6m, "USD", 10m, "CAP-1", null));

        await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(4m, "key-1"));
        await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(6m, "key-2"));

        // beyond the captured amount → refused before any provider call
        await Assert.ThrowsAsync<OrderStateException>(() =>
            _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(0.01m, "key-3")));
        await _gateway.DidNotReceive().RefundAsync("CAP-1", 0.01m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_never_reveals_another_shoppers_order()
    {
        await AuthorizeAndCapture();

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            _service.RefundAsync(_fulfilledOrderId, OtherBuyer, false, new RefundCommand(1m, "key-x")));
        _gateway.RefundAsync("CAP-1", 1m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-ADMIN", RefundStatuses.Completed, 1m, "USD", 1m, "CAP-1", "PP-ORDER-1"));
        // an operator (admin) may act on any order
        var result = await _service.RefundAsync(_fulfilledOrderId, "admin@microsoft.com", true, new RefundCommand(1m, "key-admin"));
        Assert.Equal("key-admin", result.Refund.IdempotencyKey);
    }

    [Fact]
    public async Task Refund_after_unknown_outcome_settles_from_provider_state_instead_of_double_refunding()
    {
        await AuthorizeAndCapture();
        // The hold invoice reference is generated (unique per hold); the refund reference is
        // that plus "-r-{key}", so the crash-settle match works off the deterministic suffix.
        _gateway.GetOrderSnapshotAsync("PP-ORDER-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayOrderSnapshot("PP-ORDER-1", "COMPLETED",
                new List<GatewayAuthorization>(),
                new List<GatewayCapture>(),
                new List<GatewayRefund> { new("REF-SAVED", RefundStatuses.Completed, 3m, "USD", 3m, "CAP-1", "PP-ORDER-1", "eshop-order-9-ab12cd34ef-r-key-crash") }));

        var result = await _service.RefundAsync(_fulfilledOrderId, Buyer, false, new RefundCommand(3m, "key-crash"));

        Assert.True(result.Replayed);
        Assert.Equal("REF-SAVED", result.Refund.ProviderRefundId);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MyOrders_returns_only_the_callers_orders_with_payment_state()
    {
        var mine = await PlacedOrder();
        GatewayAuthorizes();
        await _service.PayAsync(mine.Id, Buyer, CardPay());
        await PlacedOrder(buyerId: OtherBuyer);

        var orders = await _service.GetOrdersForBuyerAsync(Buyer);

        var order = Assert.Single(orders);
        Assert.Equal(mine.Id, order.Id);
        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.NotNull(order.Payment);
    }

    // ---------- helpers ----------

    private int _fulfilledOrderId;

    private static PayCommand CardPay() =>
        new(new CardCredential("4111111111111111", "09/2029", "123", "Test Buyer", null), null, null);

    private async Task AuthorizeAndCapture()
    {
        var order = await PlacedOrder();
        GatewayAuthorizes();
        await _service.PayAsync(order.Id, Buyer, CardPay());
        _gateway.CaptureAsync("AUTH-1", 10m, "USD", Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-1", CaptureStatuses.Completed, null, 10m, 0.6m, 9.4m, "USD", "AUTH-1", "PP-ORDER-1"));
        await _service.FulfilAsync(order.Id);
        _fulfilledOrderId = order.Id;
    }
}
