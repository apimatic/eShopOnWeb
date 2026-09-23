using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

/// <summary>
/// Deterministic tests of the order-payment orchestration using in-memory EF repositories and a faked
/// <see cref="IPayPalGateway"/> — the money-movement guarantees the task requires, with no PayPal traffic.
/// </summary>
public class PaymentServiceTests
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();
    private readonly PaymentService _service;
    private const string Buyer = "buyer@example.com";
    private const string OtherBuyer = "someone-else@example.com";

    public PaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("PaymentServiceTests-" + Guid.NewGuid())
            .Options;
        _db = new CatalogContext(options);

        var uri = Substitute.For<IUriComposer>();
        uri.ComposePicUri(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        var settings = Options.Create(new PayPalSettings
        {
            ClientId = "x", ClientSecret = "y", Environment = "sandbox", Currency = "USD",
        });

        _service = new PaymentService(
            new EfRepository<Order>(_db),
            new EfRepository<CatalogItem>(_db),
            new EfRepository<OrderPayment>(_db),
            new EfRepository<SavedCard>(_db),
            _gateway,
            uri,
            settings,
            Substitute.For<ILogger<PaymentService>>());
    }

    private async Task<int> SeedCatalogItemAsync(decimal price)
    {
        var item = new CatalogItem(1, 1, "desc", "Widget", price, "pic.png");
        _db.CatalogItems.Add(item);
        await _db.SaveChangesAsync();
        return item.Id;
    }

    private void GatewayAuthorizes() =>
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER", "AUTH-1", AuthorizationOutcome.Authorized,
                "CREATED", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow, null));

    private static PayInput Card() =>
        new(new CardDetails("4111111111111111", "2030-01", "123", "Tester"), null);

    [Fact]
    public async Task PlaceOrder_CreatesOrderAndAwaitingPayment()
    {
        var itemId = await SeedCatalogItemAsync(10m);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 2) }, null, default);

        var view = (await _service.GetMyOrdersAsync(Buyer, default))[0];
        Assert.Equal(orderId, view.OrderId);
        Assert.Equal("AwaitingPayment", view.Status);
        Assert.Equal(20m, view.Amount);
    }

    [Fact]
    public async Task PlaceOrder_RejectsUnknownCatalogItem()
    {
        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(999999, 1) }, null, default));
    }

    [Fact]
    public async Task Authorize_IsIdempotent_SecondCallDoesNotHitPayPalAgain()
    {
        GatewayAuthorizes();
        var itemId = await SeedCatalogItemAsync(15m);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 1) }, null, default);

        var first = await _service.AuthorizeAsync(Buyer, orderId, Card(), default);
        var second = await _service.AuthorizeAsync(Buyer, orderId, Card(), default);

        Assert.Equal("Authorized", first.Status);
        Assert.Equal("Authorized", second.Status);
        Assert.Equal("AUTH-1", second.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<AuthorizeGatewayRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_ChallengeRequired_Throws()
    {
        _gateway.AuthorizeAsync(Arg.Any<AuthorizeGatewayRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP", null, AuthorizationOutcome.ChallengeRequired, null, null, null, "3ds"));
        var itemId = await SeedCatalogItemAsync(15m);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 1) }, null, default);

        await Assert.ThrowsAsync<PaymentChallengeRequiredException>(() =>
            _service.AuthorizeAsync(Buyer, orderId, Card(), default));
    }

    [Fact]
    public async Task Authorize_AnotherShoppersOrder_NotFound()
    {
        GatewayAuthorizes();
        var itemId = await SeedCatalogItemAsync(15m);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 1) }, null, default);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            _service.AuthorizeAsync(OtherBuyer, orderId, Card(), default));
    }

    [Fact]
    public async Task Refund_ExceedingCaptured_IsRejected()
    {
        var orderId = await AuthorizeAndFulfilAsync(40m);
        await Assert.ThrowsAsync<PaymentValidationException>(() =>
            _service.RefundAsync(Buyer, orderId, 100m, "key-a", default));
    }

    [Fact]
    public async Task Refund_SameKeyTwice_RefundsOnce()
    {
        var orderId = await AuthorizeAndFulfilAsync(40m);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REFUND-1", RefundOutcome.Completed, "COMPLETED", 10m, "USD"));

        var r1 = await _service.RefundAsync(Buyer, orderId, 10m, "key-dup", default);
        var r2 = await _service.RefundAsync(Buyer, orderId, 10m, "key-dup", default);

        Assert.Equal("REFUND-1", r1.RefundId);
        Assert.Equal("REFUND-1", r2.RefundId);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctPartials_BothProceed()
    {
        var orderId = await AuthorizeAndFulfilAsync(40m);
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("R", RefundOutcome.Completed, "COMPLETED", 5m, "USD"));

        await _service.RefundAsync(Buyer, orderId, 5m, "k1", default);
        await _service.RefundAsync(Buyer, orderId, 7m, "k2", default);

        await _gateway.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var view = (await _service.GetMyOrdersAsync(Buyer, default))[0];
        Assert.Equal(12m, view.RefundedAmount);
        Assert.Equal("PartiallyRefunded", view.Status);
    }

    [Fact]
    public async Task Cancel_BeforeFulfil_VoidsAndMarksCancelled()
    {
        GatewayAuthorizes();
        var itemId = await SeedCatalogItemAsync(20m);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 1) }, null, default);
        await _service.AuthorizeAsync(Buyer, orderId, Card(), default);

        var view = await _service.CancelAsync(orderId, default);

        Assert.Equal("Cancelled", view.Status);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<CancellationToken>());
    }

    private async Task<int> AuthorizeAndFulfilAsync(decimal price)
    {
        GatewayAuthorizes();
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", CaptureOutcome.Completed, "COMPLETED", price, 2m, price - 2m, "USD", DateTimeOffset.UtcNow));

        var itemId = await SeedCatalogItemAsync(price);
        var orderId = await _service.PlaceOrderAsync(Buyer, new[] { new OrderLineInput(itemId, 1) }, null, default);
        await _service.AuthorizeAsync(Buyer, orderId, Card(), default);
        await _service.FulfilAsync(orderId, default);
        return orderId;
    }
}
