using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Payments;

/// <summary>
/// Deterministic tests of the orchestration invariants, with the PayPal gateway faked so no network is
/// touched: totals, idempotent authorize/refund, capture financials, refund caps, cancel, and ownership.
/// </summary>
public class OrderPaymentServiceTests
{
    private readonly CatalogContext _context;
    private readonly EfRepository<Order> _orders;
    private readonly EfRepository<Payment> _payments;
    private readonly EfRepository<CatalogItem> _catalog;
    private readonly EfRepository<Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate.SavedPaymentMethod> _cards;
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly OrderPaymentService _service;
    private const string Buyer = "shopper@example.com";

    public OrderPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
            .Options;
        _context = new CatalogContext(options);
        _orders = new EfRepository<Order>(_context);
        _payments = new EfRepository<Payment>(_context);
        _catalog = new EfRepository<CatalogItem>(_context);
        _cards = new EfRepository<Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate.SavedPaymentMethod>(_context);

        var settings = Options.Create(new PayPalSettings { Currency = "USD", ClientId = "x", ClientSecret = "x", Environment = "sandbox" });
        _service = new OrderPaymentService(_orders, _payments, _catalog, _cards, _gateway, settings, NullLogger<OrderPaymentService>.Instance);
    }

    private async Task<(int id5, int id10)> SeedCatalogAsync()
    {
        var a = await _catalog.AddAsync(new CatalogItem(1, 1, "d", "Item5", 5.00m, "p.png"));
        var b = await _catalog.AddAsync(new CatalogItem(1, 1, "d", "Item10", 10.00m, "p.png"));
        return (a.Id, b.Id);
    }

    private static ShippingAddressInput Address() => new("1 Main", "Redmond", "WA", "US", "98052");

    private async Task<int> PlaceAsync(params (int id, int qty)[] lines)
    {
        var summary = await _service.PlaceOrderAsync(Buyer, lines.Select(l => new OrderLineInput(l.id, l.qty)).ToList(), Address());
        return summary.OrderId;
    }

    [Fact]
    public async Task PlaceOrder_ComputesTotalFromCatalogPrices()
    {
        var (id5, id10) = await SeedCatalogAsync();

        var summary = await _service.PlaceOrderAsync(Buyer,
            new[] { new OrderLineInput(id5, 2), new OrderLineInput(id10, 1) }, Address());

        Assert.Equal(20.00m, summary.Total); // 5*2 + 10*1
        Assert.Equal("PendingPayment", summary.PaymentStatus);
        Assert.Equal("USD", summary.CurrencyCode);
    }

    [Fact]
    public async Task Authorize_IsIdempotent_DoesNotHoldTwice()
    {
        var (id5, _) = await SeedCatalogAsync();
        var orderId = await PlaceAsync((id5, 1));
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", "AUTH-1", "CREATED"));

        var first = await _service.AuthorizeAsync(Buyer, orderId, new PaymentInstrument(Card(), null));
        var second = await _service.AuthorizeAsync(Buyer, orderId, new PaymentInstrument(Card(), null));

        Assert.Equal("Authorized", first.PaymentStatus);
        Assert.Equal("AUTH-1", second.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_RecordsCapturedGrossFeeAndNet()
    {
        var (id5, _) = await SeedCatalogAsync();
        var orderId = await PlaceAsync((id5, 1));
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", "AUTH-1", "CREATED"));
        _gateway.CaptureAsync(Arg.Any<CaptureCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 5.00m, 0.45m, 4.55m, null));
        await _service.AuthorizeAsync(Buyer, orderId, new PaymentInstrument(Card(), null));

        var summary = await _service.FulfilAsync(orderId);

        Assert.Equal("Captured", summary.PaymentStatus);
        Assert.Equal("CAP-1", summary.CaptureId);
        Assert.Equal(5.00m, summary.CapturedGross);
        Assert.Equal(0.45m, summary.PayPalFee);
        Assert.Equal(4.55m, summary.NetAmount);
    }

    [Fact]
    public async Task Fulfil_BeforeAuthorize_IsRejected()
    {
        var (id5, _) = await SeedCatalogAsync();
        var orderId = await PlaceAsync((id5, 1));

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() => _service.FulfilAsync(orderId));
    }

    [Fact]
    public async Task Refund_SameKey_DoesNotRefundTwice()
    {
        var orderId = await CapturedOrderAsync(10.00m);
        _gateway.RefundAsync(Arg.Any<RefundCommand>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF-1", "COMPLETED", 4.00m));

        var (s1, r1) = await _service.RefundAsync(Buyer, orderId, 4.00m, "key-1");
        var (s2, r2) = await _service.RefundAsync(Buyer, orderId, 4.00m, "key-1");

        Assert.Equal("REF-1", r1);
        Assert.Equal(r1, r2);
        Assert.Equal(4.00m, s2.RefundedAmount);
        await _gateway.Received(1).RefundAsync(Arg.Any<RefundCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctPartials_BothApply_UntilCapReached()
    {
        var orderId = await CapturedOrderAsync(10.00m);
        _gateway.RefundAsync(Arg.Any<RefundCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RefundResult(Guid.NewGuid().ToString("N"), "COMPLETED", ((RefundCommand)ci[0]).Amount!.Value));

        await _service.RefundAsync(Buyer, orderId, 4.00m, "key-1");
        var (s2, _) = await _service.RefundAsync(Buyer, orderId, 3.00m, "key-2");
        Assert.Equal(7.00m, s2.RefundedAmount);
        Assert.Equal(3.00m, s2.RefundableAmount);

        // Over the remaining cap → rejected, and PayPal is not called for it.
        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() => _service.RefundAsync(Buyer, orderId, 5.00m, "key-3"));
        await _gateway.Received(2).RefundAsync(Arg.Any<RefundCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_VoidsAuthorization_WhenAuthorized()
    {
        var (id5, _) = await SeedCatalogAsync();
        var orderId = await PlaceAsync((id5, 1));
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", "AUTH-1", "CREATED"));
        await _service.AuthorizeAsync(Buyer, orderId, new PaymentInstrument(Card(), null));

        var summary = await _service.CancelAsync(orderId);

        Assert.Equal("Canceled", summary.PaymentStatus);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Operations_OnAnotherShoppersOrder_AreNotFound()
    {
        var (id5, _) = await SeedCatalogAsync();
        var orderId = await PlaceAsync((id5, 1));

        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => _service.AuthorizeAsync("intruder@example.com", orderId, new PaymentInstrument(Card(), null)));
        await Assert.ThrowsAsync<PaymentNotFoundException>(
            () => _service.RefundAsync("intruder@example.com", orderId, 1m, "k"));
    }

    private static CardDetails Card() => new("Shopper", "4111111111111111", "2030-01", "123", "US", "98052");

    private async Task<int> CapturedOrderAsync(decimal amount)
    {
        var item = await _catalog.AddAsync(new CatalogItem(1, 1, "d", "Item", amount, "p.png"));
        var orderId = await PlaceAsync((item.Id, 1));
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO", "AUTH", "CREATED"));
        _gateway.CaptureAsync(Arg.Any<CaptureCommand>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP", "COMPLETED", amount, 0.5m, amount - 0.5m, null));
        await _service.AuthorizeAsync(Buyer, orderId, new PaymentInstrument(Card(), null));
        await _service.FulfilAsync(orderId);
        return orderId;
    }
}
