using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class FulfilOrder
{
    private const string BuyerId = "buyer-1";
    private const int OrderId = 7;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedPaymentMethod> _savedMethodRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() => new PaymentService(
        _orderRepo, _paymentRepo, _savedMethodRepo, _gateway,
        new PayPalOptions { Currency = "USD" }, _logger);

    private static Order CreateAuthorizedOrder(out Payment payment)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://img"), 25m, 1);
        var order = new OrderWithId(BuyerId, new Address("Main st", "City", "ST", "Country", "12345"), new List<OrderItem> { item }, OrderId);
        order.MarkPaymentAuthorized();
        payment = new Payment(OrderId, BuyerId, order.Total(), "USD");
        payment.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3));
        return order;
    }

    private void Given(Order order, Payment? payment)
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>()).Returns(payment);
    }

    [Fact]
    public async Task CapturesAndRecordsFeeBreakdown()
    {
        var order = CreateAuthorizedOrder(out var payment);
        Given(order, payment);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), 25m));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 25m, 1.15m, 23.85m, "USD"));

        var result = await CreateService().FulfilAsync(OrderId, default);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP-1", result.CaptureId);
        Assert.Equal(25m, result.CapturedAmount);
        Assert.Equal(1.15m, result.PayPalFee);
        Assert.Equal(23.85m, result.NetAmount);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task ReturnsExistingCaptureOnReplayWithoutCapturingAgain()
    {
        var order = CreateAuthorizedOrder(out var payment);
        payment.MarkCaptured("CAP-1", 25m, 1.15m, 23.85m, "COMPLETED");
        order.MarkFulfilled();
        Given(order, payment);

        var result = await CreateService().FulfilAsync(OrderId, default);

        Assert.Same(payment, result);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenewsStaleAuthorizationBeforeCapturing()
    {
        var order = CreateAuthorizedOrder(out var payment);
        Given(order, payment);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1), 25m));
        _gateway.ReauthorizeAsync("AUTH-1", 25m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), 25m, "USD", null));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("CAP-1", "COMPLETED", 25m, 1.15m, 23.85m, "USD"));

        var result = await CreateService().FulfilAsync(OrderId, default);

        await _gateway.Received(1).ReauthorizeAsync("AUTH-1", 25m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(PaymentStatus.Captured, result.Status);
    }

    [Fact]
    public async Task ThrowsActionableErrorWhenAuthorizationCannotBeRenewed()
    {
        var order = CreateAuthorizedOrder(out var payment);
        Given(order, payment);
        _gateway.GetAuthorizationAsync("AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorizationStatus("AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(-1), 25m));
        _gateway.ReauthorizeAsync("AUTH-1", 25m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<AuthorizationResult>(_ => throw new PaymentGatewayException("max reauthorizations reached", 422));

        await Assert.ThrowsAsync<AuthorizationNotRenewableException>(() => CreateService().FulfilAsync(OrderId, default));

        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public async Task ThrowsConflictWhenOrderNotPaid()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://img"), 25m, 1);
        var order = new OrderWithId(BuyerId, new Address("Main st", "City", "ST", "Country", "12345"), new List<OrderItem> { item }, OrderId);
        Given(order, null);

        await Assert.ThrowsAsync<PaymentStateConflictException>(() => CreateService().FulfilAsync(OrderId, default));
    }

    private class OrderWithId : Order
    {
        public OrderWithId(string buyerId, Address address, List<OrderItem> items, int id)
            : base(buyerId, address, items)
        {
            Id = id;
        }
    }
}
