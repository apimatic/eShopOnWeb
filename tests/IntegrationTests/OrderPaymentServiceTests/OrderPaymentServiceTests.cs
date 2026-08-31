using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.OrderPaymentServiceTests;

public class OrderPaymentServiceTests
{
    private const string BuyerId = "shopper@example.com";

    private readonly CatalogContext _context;
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly OrderPaymentService _service;

    public OrderPaymentServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new CatalogContext(dbOptions);

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://localhost/pic");

        _service = new OrderPaymentService(
            new EfRepository<Order>(_context),
            new EfRepository<CatalogItem>(_context),
            new EfRepository<Payment>(_context),
            new EfRepository<SavedCard>(_context),
            _gateway,
            uriComposer,
            Options.Create(new PayPalSettings { Currency = "USD" }));
    }

    private async Task<Order> CreateOrderAsync(decimal unitPrice = 10m, int units = 2)
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "item", unitPrice, "pic.png");
        _context.CatalogItems.Add(catalogItem);
        _context.SaveChanges();

        return await _service.CreateOrderAsync(
            BuyerId,
            new List<OrderItemRequest> { new OrderItemRequest(catalogItem.Id, units) },
            new Address("1 Main St", "City", "ST", "US", "12345"));
    }

    private void SetupGatewayForAuthorizeAndCapture()
    {
        _gateway.AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new GatewayAuthorization("PPO-1", "AUTH-1", "CREATED",
                ci.Arg<GatewayAuthorizeRequest>().Amount, "USD", DateTimeOffset.UtcNow.AddDays(29)));
        _gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationState("AUTH-1", "CREATED", 20m, "USD", DateTimeOffset.UtcNow.AddDays(29)));
        _gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-1", "COMPLETED", 20m, 0.88m, 19.12m, "USD"));
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => new GatewayRefund($"REF-{ci.ArgAt<string>(3)}", "COMPLETED", ci.ArgAt<decimal?>(1), "USD"));
        _gateway.VoidAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationState("AUTH-1", "VOIDED", 20m, "USD", null));
    }

    private async Task<Order> CreateCapturedOrderAsync()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);
        await _service.AuthorizeAsync(BuyerId, order.Id, card, null);
        await _service.FulfilAsync(order.Id);
        return order;
    }

    [Fact]
    public async Task Authorize_HoldsOrderTotalAndMarksOrderAuthorized()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);

        var payment = await _service.AuthorizeAsync(BuyerId, order.Id, card, null);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(20m, payment.AuthorizedAmount);
        Assert.Equal("AUTH-1", payment.AuthorizationId);

        var reloaded = await _service.ListOrdersAsync(BuyerId);
        Assert.Equal(OrderStatus.Authorized, reloaded[0].Status);
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_DoesNotCallGatewayAgain()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);

        await _service.AuthorizeAsync(BuyerId, order.Id, card, null);
        var second = await _service.AuthorizeAsync(BuyerId, order.Id, card, null);

        Assert.Equal("AUTH-1", second.AuthorizationId);
        await _gateway.Received(1).AuthorizeAsync(Arg.Any<GatewayAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OtherShoppersOrder_ThrowsNotFound()
    {
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _service.AuthorizeAsync("someone-else@example.com", order.Id, card, null));
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        var order = await CreateCapturedOrderAsync();

        var orders = await _service.ListOrdersAsync(BuyerId);
        var payment = orders[0].Payment!;

        Assert.Equal(OrderStatus.Fulfilled, orders[0].Status);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal("CAP-1", payment.CaptureId);
        Assert.Equal(20m, payment.CapturedAmount);
        Assert.Equal(0.88m, payment.PaypalFee);
        Assert.Equal(19.12m, payment.NetAmount);
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationExpired_RenewsThenCaptures()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);
        await _service.AuthorizeAsync(BuyerId, order.Id, card, null);

        _gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationState("AUTH-1", "EXPIRED", 20m, "USD", DateTimeOffset.UtcNow.AddDays(-1)));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationState("AUTH-2", "CREATED", 20m, "USD", DateTimeOffset.UtcNow.AddDays(29)));

        await _service.FulfilAsync(order.Id);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", 20m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).CaptureAsync("AUTH-2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenAuthorizationCannotBeRenewed_ThrowsOperatorActionableError()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);
        await _service.AuthorizeAsync(BuyerId, order.Id, card, null);

        _gateway.GetAuthorizationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationState("AUTH-1", "EXPIRED", 20m, "USD", DateTimeOffset.UtcNow.AddDays(-1)));
        _gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GatewayAuthorizationState>>(_ => throw new PaymentGatewayException("PayPal reauthorize rejected the request", isProviderRejection: true));

        var ex = await Assert.ThrowsAsync<PaymentConflictException>(() => _service.FulfilAsync(order.Id));
        Assert.Contains("can no longer be renewed", ex.Message);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_AuthorizedOrder_VoidsHoldAtGateway()
    {
        SetupGatewayForAuthorizeAndCapture();
        var order = await CreateOrderAsync();
        var card = new GatewayCardDetails("4111111111111111", "2030-12", "123", "Test", null);
        await _service.AuthorizeAsync(BuyerId, order.Id, card, null);

        var cancelled = await _service.CancelAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_SameIdempotencyKeyTwice_RefundsOnlyOnce()
    {
        var order = await CreateCapturedOrderAsync();

        var first = await _service.RefundAsync(BuyerId, order.Id, 5m, "key-1");
        var second = await _service.RefundAsync(BuyerId, order.Id, 5m, "key-1");

        Assert.Equal(first.PayPalRefundId, second.PayPalRefundId);
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctPartialRefunds_BothGoThrough()
    {
        var order = await CreateCapturedOrderAsync();

        await _service.RefundAsync(BuyerId, order.Id, 5m, "key-1");
        await _service.RefundAsync(BuyerId, order.Id, 3m, "key-2");

        await _gateway.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var orders = await _service.ListOrdersAsync(BuyerId);
        Assert.Equal(12m, orders[0].Payment!.RefundableAmount());
        Assert.Equal(PaymentStatus.PartiallyRefunded, orders[0].Payment!.Status);
    }

    [Fact]
    public async Task Refund_BeyondCapturedAmount_NeverCallsGateway()
    {
        var order = await CreateCapturedOrderAsync();
        await _service.RefundAsync(BuyerId, order.Id, 5m, "key-1");

        await Assert.ThrowsAsync<PaymentConflictException>(
            () => _service.RefundAsync(BuyerId, order.Id, 16m, "key-2"));

        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_DuplicateKeyAtProvider_IsNotRetriedAndSurfacesConflict()
    {
        var order = await CreateCapturedOrderAsync();
        _gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<GatewayRefund>>(_ => throw new PaymentGatewayException("duplicate", isProviderRejection: true, providerErrorName: "DUPLICATE_REQUEST_ID"));

        await Assert.ThrowsAsync<PaymentConflictException>(() => _service.RefundAsync(BuyerId, order.Id, 5m, "key-1"));

        // The key is now parked: repeating it must not call the provider again.
        await Assert.ThrowsAsync<PaymentConflictException>(() => _service.RefundAsync(BuyerId, order.Id, 5m, "key-1"));
        await _gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
