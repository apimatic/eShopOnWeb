using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private readonly CardDetails _card = new("Demo User", "4111111111111111", 12, 2030, "123", null);

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _gateway, _uriComposer);

    private PaymentInstruction OneOffCard => new(_card, null);

    private static PaymentAuthorization Auth() =>
        new("PPO-1", "CAP-1", new CardDisplay("VISA", "1111", 12, 2030));

    [Fact]
    public async Task ChargesAndMarksPaidWhenAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.ChargeCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Auth());

        var result = await CreateService().PayOrderAsync(BuyerId, 1, OneOffCard);

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal("CAP-1", result.PayPalCaptureId);
        await _gateway.Received(1).ChargeCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotChargeAgainWhenAlreadyPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().PayOrderAsync(BuyerId, 1, OneOffCard);

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        await _gateway.DidNotReceive().ChargeCardAsync(Arg.Any<Money>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenOrderRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", "VISA");
        order.MarkRefunded("REF-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentException>(() => CreateService().PayOrderAsync(BuyerId, 1, OneOffCard));
    }

    [Fact]
    public async Task ThrowsWhenOrderNotFound()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => CreateService().PayOrderAsync(BuyerId, 99, OneOffCard));
    }

    [Fact]
    public async Task ThrowsWhenInstructionHasNeitherCardNorSavedCard()
    {
        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PaymentInstruction(null, null)));
    }

    [Fact]
    public async Task ThrowsWhenSavedCardNotOwnedByBuyer()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdAndBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns((PaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentException>(() =>
            CreateService().PayOrderAsync(BuyerId, 1, new PaymentInstruction(null, 7)));
        await _gateway.DidNotReceive().ChargeVaultedCardAsync(Arg.Any<Money>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChargesVaultedCardWhenSavedCardProvided()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var pm = new PaymentMethod(BuyerId, "VAULT-1", "VISA", "1111", 12, 2030, "Demo User");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdAndBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns(pm);
        _gateway.ChargeVaultedCardAsync(Arg.Any<Money>(), "VAULT-1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Auth());

        var result = await CreateService().PayOrderAsync(BuyerId, 1, new PaymentInstruction(null, 5));

        Assert.Equal(OrderPaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal(pm.Description, result.PaymentCardDescription);
        await _gateway.Received(1).ChargeVaultedCardAsync(Arg.Any<Money>(), "VAULT-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
