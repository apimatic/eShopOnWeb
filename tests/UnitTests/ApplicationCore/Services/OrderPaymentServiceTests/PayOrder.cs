using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PayOrder
{
    private const string BuyerId = "12345";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<PaymentMethod> _pmRepo = Substitute.For<IReadRepository<PaymentMethod>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _uriComposer, _gateway);

    private static CardDetails ValidCard() => new("4111111111111111", "2030-01", "123", "Tester", null);

    [Fact]
    public async Task ChargesCardAndMarksOrderPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.CaptureCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CapturedPayment("PPO-1", "CAP-1", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(ValidCard(), null));

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal("CAP-1", result.PaymentCaptureId);
        await _gateway.Received(1).CaptureCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<RawCardSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyPaidOrderIsNotChargedAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PPO-1", "CAP-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(ValidCard(), null));

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.DidNotReceive().CaptureCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownOrderThrowsNotFound()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            CreateService().PayOrderAsync(BuyerId, 99, new PayOrderCommand(ValidCard(), null)));
    }

    [Fact]
    public async Task SavedCardNotOwnedThrowsNotFound()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodForBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns((PaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(null, 7)));
        await _gateway.DidNotReceive().CaptureCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavedCardChargesViaVaultSource()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod(BuyerId, "VAULT-9", "VISA", "1111", "Tester", "2030-01"));
        _gateway.CaptureCardPaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardPaymentSource>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CapturedPayment("PPO-2", "CAP-2", "COMPLETED"));

        var result = await CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(null, 7));

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.Received(1).CaptureCardPaymentAsync(
            Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Is<VaultedCardSource>(v => v.VaultId == "VAULT-9"),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeitherCardNorSavedIdThrowsInputError()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentInputException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(null, null)));
    }

    [Fact]
    public async Task BothCardAndSavedIdThrowsInputError()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentInputException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(ValidCard(), 7)));
    }

    [Fact]
    public async Task RefundedOrderCannotBePaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PPO-1", "CAP-1");
        order.MarkAsRefunded("REF-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PayOrderCommand(ValidCard(), null)));
    }
}
