using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _pmRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderPaymentService> _logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    private readonly string _buyerId = "12345";

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _gateway, _uriComposer, _logger);

    private static CardDetails ValidCard() => new()
    {
        Number = "4111111111111111",
        ExpiryMonthYear = "2030-01",
        SecurityCode = "123"
    };

    [Fact]
    public async Task ChargesCardAndMarksOrderPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.ChargeCardAsync(Arg.Any<CardChargeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayPaymentResult { Success = true, PayPalOrderId = "PP-ORDER", CaptureId = "PP-CAPTURE", Status = "COMPLETED" });

        var result = await CreateService().PayOrderAsync(_buyerId, 1, new OrderPaymentInput { Card = ValidCard() });

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("PP-CAPTURE", order.PayPalCaptureId);
        await _gateway.Received(1).ChargeCardAsync(Arg.Any<CardChargeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsIdempotent_ReturnsAlreadyPaidWithoutChargingAgain()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PP-ORDER", "PP-CAPTURE");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().PayOrderAsync(_buyerId, 1, new OrderPaymentInput { Card = ValidCard() });

        Assert.Equal(PayOrderOutcome.AlreadyPaid, result.Outcome);
        await _gateway.DidNotReceive().ChargeCardAsync(Arg.Any<CardChargeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsOrderNotFound_WhenOrderBelongsToAnotherShopper()
    {
        var order = new OrderBuilder().WithDefaultValues(); // BuyerId = "12345"
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().PayOrderAsync("a-different-shopper", 1, new OrderPaymentInput { Card = ValidCard() });

        Assert.Equal(PayOrderOutcome.OrderNotFound, result.Outcome);
        await _gateway.DidNotReceive().ChargeCardAsync(Arg.Any<CardChargeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsInvalidRequest_WhenBothCardAndSavedCardProvided()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().PayOrderAsync(_buyerId, 1,
            new OrderPaymentInput { Card = ValidCard(), SavedPaymentMethodId = 7 });

        Assert.Equal(PayOrderOutcome.InvalidRequest, result.Outcome);
    }

    [Fact]
    public async Task ReturnsSavedCardNotFound_WhenSavedCardMissingForShopper()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _pmRepo.FirstOrDefaultAsync(Arg.Any<PaymentMethodByIdForOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);

        var result = await CreateService().PayOrderAsync(_buyerId, 1, new OrderPaymentInput { SavedPaymentMethodId = 7 });

        Assert.Equal(PayOrderOutcome.SavedCardNotFound, result.Outcome);
        await _gateway.DidNotReceive().ChargeVaultedCardAsync(Arg.Any<VaultedCardChargeRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsPaymentFailed_AndLeavesOrderAwaitingPayment_WhenGatewayDeclines()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.ChargeCardAsync(Arg.Any<CardChargeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayPaymentResult { Success = false, Status = "DECLINED", ErrorMessage = "declined" });

        var result = await CreateService().PayOrderAsync(_buyerId, 1, new OrderPaymentInput { Card = ValidCard() });

        Assert.Equal(PayOrderOutcome.PaymentFailed, result.Outcome);
        Assert.Equal(PaymentStatus.AwaitingPayment, order.PaymentStatus);
    }
}
