using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

/// <summary>
/// Deterministic coverage for the payment orchestration paths that a live sandbox run cannot force
/// in a short session: stale-hold renewal, a hold that can no longer be renewed, refund idempotency,
/// over-refund protection, and cross-shopper isolation.
/// </summary>
public class OrderPaymentServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _cards = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();
    private readonly IPaymentSettings _settings = Substitute.For<IPaymentSettings>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService()
    {
        _settings.Currency.Returns("USD");
        return new OrderPaymentService(_orders, _items, _cards, _payPal, _settings, _uri, _logger);
    }

    private static Order AuthorizedOrder(string buyer, decimal amount, DateTimeOffset? expiresAt)
    {
        var items = new List<OrderItem> { new(new CatalogItemOrdered(1, "item", "pic.png"), amount, 1) };
        var order = new Order(buyer, new Address("s", "c", "st", "co", "z"), items);
        order.MarkAuthorized(new Payment("PPO", "AUTH1", "CREATED", amount, "USD", expiresAt, null));
        return order;
    }

    private static Order CapturedOrder(string buyer, decimal amount)
    {
        var order = AuthorizedOrder(buyer, amount, DateTimeOffset.UtcNow.AddDays(3));
        order.Payment!.RecordCapture("CAP1", "COMPLETED", amount, 3m, amount - 3m);
        order.MarkFulfilled();
        return order;
    }

    private void ReturnsOrder(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>())
               .Returns(order);

    [Fact]
    public async Task Refund_WithSameIdempotencyKey_DoesNotRefundTwice()
    {
        var order = CapturedOrder("buyer@test", 100m);
        ReturnsOrder(order);
        _payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new PayPalRefund { RefundId = "R1", Status = "COMPLETED", Amount = 10m, Currency = "USD" });
        var service = CreateService();

        var first = await service.RefundAsync("buyer@test", 1, 10m, "key-1");
        var second = await service.RefundAsync("buyer@test", 1, 10m, "key-1");

        Assert.Equal("R1", first.RefundId);
        Assert.Equal(first.RefundId, second.RefundId);
        // Only one actual refund reached PayPal despite two requests under the same key.
        await _payPal.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_TwoDistinctPartials_UnderDistinctKeys_BothProceed()
    {
        var order = CapturedOrder("buyer@test", 100m);
        ReturnsOrder(order);
        _payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ci => new PayPalRefund { RefundId = Guid.NewGuid().ToString("N"), Status = "COMPLETED", Amount = (decimal)ci[1]!, Currency = "USD" });
        var service = CreateService();

        var r1 = await service.RefundAsync("buyer@test", 1, 30m, "key-a");
        var r2 = await service.RefundAsync("buyer@test", 1, 40m, "key-b");

        Assert.NotEqual(r1.RefundId, r2.RefundId);
        await _payPal.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_BeyondCapturedAmount_IsRejected_AndNeverCallsPayPal()
    {
        var order = CapturedOrder("buyer@test", 100m);
        ReturnsOrder(order);
        var service = CreateService();

        await Assert.ThrowsAsync<PaymentStateException>(() => service.RefundAsync("buyer@test", 1, 150m, "key-x"));
        await _payPal.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_PartialThenRemainder_CannotExceedCapture()
    {
        var order = CapturedOrder("buyer@test", 100m);
        ReturnsOrder(order);
        _payPal.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(ci => new PayPalRefund { RefundId = Guid.NewGuid().ToString("N"), Status = "COMPLETED", Amount = ((decimal?)ci[1]) ?? 40m, Currency = "USD" });
        var service = CreateService();

        await service.RefundAsync("buyer@test", 1, 60m, "key-a");
        // 60 already refunded; a further 60 would exceed the 100 captured.
        await Assert.ThrowsAsync<PaymentStateException>(() => service.RefundAsync("buyer@test", 1, 60m, "key-b"));
    }

    [Fact]
    public async Task Fulfil_WithStaleHold_RenewsThenCaptures()
    {
        var order = AuthorizedOrder("buyer@test", 50m, DateTimeOffset.UtcNow.AddMinutes(-1)); // already stale
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new PayPalAuthorization { PayPalOrderId = "PPO", AuthorizationId = "AUTH2", Status = "CREATED", Amount = 50m, Currency = "USD", ExpiresAt = DateTimeOffset.UtcNow.AddDays(3) });
        _payPal.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new PayPalCapture { CaptureId = "CAP2", Status = "COMPLETED", GrossAmount = 50m, PayPalFee = 2m, NetAmount = 48m, Currency = "USD" });
        var service = CreateService();

        var result = await service.FulfilAsync(1);

        Assert.Equal(OrderStatus.Fulfilled, result.Status);
        Assert.Equal("AUTH2", result.Payment!.AuthorizationId); // renewed
        Assert.Equal("CAP2", result.Payment.CaptureId);
        await _payPal.Received(1).ReauthorizeAsync("AUTH1", 50m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payPal.Received(1).CaptureAsync("AUTH2", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fulfil_WhenHoldCannotBeRenewed_SurfacesActionableError()
    {
        var order = AuthorizedOrder("buyer@test", 50m, DateTimeOffset.UtcNow.AddMinutes(-1));
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _payPal.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<PayPalAuthorization>(_ => throw new AuthorizationNotRenewableException("Honor period elapsed; create a new authorization."));
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() => service.FulfilAsync(1));
        Assert.Contains("Honor period", ex.Message);
        await _payPal.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_OnOrderNotOwnedByCaller_IsNotFound()
    {
        // The buyer-scoped spec finds nothing → the order is invisible to this shopper.
        _orders.FirstOrDefaultAsync(Arg.Any<OrderWithPaymentByIdSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            service.AuthorizeAsync("someone-else@test", 1, new PayInstruction { Card = new CardDetails { Number = "4111111111111111", ExpiryMonth = 12, ExpiryYear = 2030, SecurityCode = "123" } }));
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Authorize_WhenAlreadyAuthorized_IsIdempotentAndDoesNotReauthorize()
    {
        var order = AuthorizedOrder("buyer@test", 50m, DateTimeOffset.UtcNow.AddDays(3));
        ReturnsOrder(order);
        var service = CreateService();

        var result = await service.AuthorizeAsync("buyer@test", 1, new PayInstruction { Card = new CardDetails { Number = "4111111111111111", ExpiryMonth = 12, ExpiryYear = 2030, SecurityCode = "123" } });

        Assert.Equal(OrderStatus.Authorized, result.Status);
        await _payPal.DidNotReceive().AuthorizeAsync(Arg.Any<PayPalAuthorizeRequest>(), Arg.Any<CancellationToken>());
    }
}
