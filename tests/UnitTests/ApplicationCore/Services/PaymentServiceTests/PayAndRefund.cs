using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PayAndRefund
{
    private const string BuyerId = "12345"; // matches OrderBuilder.TestBuyerId

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IBuyerService _buyerService = Substitute.For<IBuyerService>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() => new(_orderRepo, _gateway, _buyerService, _logger);

    private void GivenOrder(Order order) =>
        _orderRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);

    private static PaymentCard AnyCard() => new("4111111111111111", 12, 2030, "123");

    [Fact]
    public async Task PayWithCardChargesGatewayAndMarksPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        GivenOrder(order);
        _gateway.ChargeCardAsync(Arg.Any<decimal>(), "USD", Arg.Any<PaymentCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayChargeResult("PPO1", "CAP1", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(BuyerId, 1, AnyCard(), null);

        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal("CAP1", result.CaptureId);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithSavedCardUsesVaultToken()
    {
        var order = new OrderBuilder().WithDefaultValues();
        GivenOrder(order);

        var buyer = new Buyer(BuyerId);
        buyer.AddPaymentMethod(new PaymentMethod("My Visa", "VAULT-TOKEN-1", "1111", "VISA", "2030-12")); // Id defaults to 0
        _buyerService.GetOrCreateBuyerAsync(BuyerId, Arg.Any<CancellationToken>()).Returns(buyer);
        _gateway.ChargeVaultedCardAsync(Arg.Any<decimal>(), "USD", "VAULT-TOKEN-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayChargeResult("PPO2", "CAP2", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(BuyerId, 1, null, 0);

        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        await _gateway.Received(1).ChargeVaultedCardAsync(Arg.Any<decimal>(), "USD", "VAULT-TOKEN-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithUnknownSavedCardThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        GivenOrder(order);
        _buyerService.GetOrCreateBuyerAsync(BuyerId, Arg.Any<CancellationToken>()).Returns(new Buyer(BuyerId));

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, null, 999));
    }

    [Fact]
    public async Task PayForAnotherBuyersOrderThrowsNotFound()
    {
        var order = new OrderBuilder().WithDefaultValues(); // BuyerId 12345
        GivenOrder(order);

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateService().PayOrderAsync("someone-else", 1, AnyCard(), null));
    }

    [Fact]
    public async Task PayingAlreadyPaidOrderDoesNotChargeAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO1", "CAP1");
        GivenOrder(order);

        var result = await CreateService().PayOrderAsync(BuyerId, 1, AnyCard(), null);

        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal("CAP1", result.CaptureId);
        await _gateway.DidNotReceive().ChargeCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<PaymentCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayWithBothCardAndSavedIdThrows()
    {
        GivenOrder(new OrderBuilder().WithDefaultValues());

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, AnyCard(), 5));
    }

    [Fact]
    public async Task RefundUnpaidOrderThrows()
    {
        GivenOrder(new OrderBuilder().WithDefaultValues());

        await Assert.ThrowsAsync<PaymentOperationException>(
            () => CreateService().RefundOrderAsync(BuyerId, 1));
    }

    [Fact]
    public async Task RefundPaidOrderMarksRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO1", "CAP1");
        GivenOrder(order);
        _gateway.RefundAsync("CAP1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF1", "COMPLETED"));

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(PaymentStatus.Refunded, result.PaymentStatus);
        Assert.Equal("REF1", result.RefundId);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
    }

    [Fact]
    public async Task RefundingAlreadyRefundedOrderDoesNotCallGatewayAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO1", "CAP1");
        order.MarkRefunded("REF1");
        GivenOrder(order);

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(PaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
