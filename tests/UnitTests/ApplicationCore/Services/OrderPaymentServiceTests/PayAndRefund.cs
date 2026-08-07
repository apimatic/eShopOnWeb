using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PayAndRefund
{
    private const string BuyerId = "buyer@test.com";

    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<SavedPaymentMethod> _savedCards = Substitute.For<IReadRepository<SavedPaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private OrderPaymentService CreateService() =>
        new(_orders, _items, _savedCards, _gateway, _uriComposer, _logger);

    private static CardDetails Card() => new() { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" };

    private void OrderReturned(Order order) =>
        _orders.FirstOrDefaultAsync(Arg.Any<OrderByIdWithItemsForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(order);

    [Fact]
    public async Task PayWithCardChargesOnceAndMarksPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturned(order);
        _gateway.ChargeCardAsync(Arg.Any<decimal>(), "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentCaptureResult("PPO-1", "CAP-1", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction { Card = Card() });

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.Received(1).ChargeCardAsync(Arg.Any<decimal>(), "USD", Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orders.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayingAnAlreadyPaidOrderDoesNotChargeAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        OrderReturned(order);

        var result = await CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction { Card = Card() });

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.DidNotReceive().ChargeCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithSavedCardUsesTheVaultToken()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturned(order);
        var saved = new SavedPaymentMethod(BuyerId, "VAULT-9", "VISA", "1111", "2030-01", null, DateTimeOffset.UtcNow);
        _savedCards.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(saved);
        _gateway.ChargeVaultedCardAsync(Arg.Any<decimal>(), "USD", "VAULT-9", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentCaptureResult("PPO-2", "CAP-2", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction { SavedPaymentMethodId = 5 });

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.Received(1).ChargeVaultedCardAsync(Arg.Any<decimal>(), "USD", "VAULT-9", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithMissingSavedCardThrowsNotFound()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturned(order);
        _savedCards.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        await Assert.ThrowsAsync<SavedPaymentMethodNotFoundException>(() =>
            CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction { SavedPaymentMethodId = 5 }));
    }

    [Fact]
    public async Task PayWithNeitherCardNorSavedCardThrowsBadRequest()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturned(order);

        await Assert.ThrowsAsync<InvalidPaymentRequestException>(() =>
            CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction()));
    }

    [Fact]
    public async Task PayForAMissingOrderThrowsNotFound()
    {
        OrderReturned(null!);

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            CreateService().PayOrderAsync(1, BuyerId, new PaymentInstruction { Card = Card() }));
    }

    [Fact]
    public async Task RefundAPaidOrderRefundsOnceAndMarksRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        OrderReturned(order);
        _gateway.RefundCaptureAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult("REF-1", "COMPLETED"));

        var result = await CreateService().RefundOrderAsync(1, BuyerId);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.Received(1).RefundCaptureAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundingAnAlreadyRefundedOrderDoesNotRefundAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", DateTimeOffset.UtcNow);
        order.MarkRefunded("REF-1", DateTimeOffset.UtcNow);
        OrderReturned(order);

        var result = await CreateService().RefundOrderAsync(1, BuyerId);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundingAnUnpaidOrderThrowsStateException()
    {
        var order = new OrderBuilder().WithDefaultValues();
        OrderReturned(order);

        await Assert.ThrowsAsync<PaymentStateException>(() => CreateService().RefundOrderAsync(1, BuyerId));
    }
}
