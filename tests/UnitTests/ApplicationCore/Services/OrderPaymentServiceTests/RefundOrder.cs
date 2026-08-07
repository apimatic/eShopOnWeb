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

public class RefundOrder
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

    private Order PaidOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PP-ORDER", "PP-CAPTURE");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    [Fact]
    public async Task RefundsCaptureAndMarksOrderRefunded()
    {
        var order = PaidOrder();
        _gateway.RefundAsync("PP-CAPTURE", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult { Success = true, RefundId = "PP-REFUND", Status = "COMPLETED" });

        var result = await CreateService().RefundOrderAsync(_buyerId, 1);

        Assert.Equal(RefundOrderOutcome.Refunded, result.Outcome);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("PP-REFUND", order.PayPalRefundId);
    }

    [Fact]
    public async Task IsIdempotent_ReturnsAlreadyRefundedWithoutRefundingAgain()
    {
        var order = PaidOrder();
        order.MarkAsRefunded("PP-REFUND");

        var result = await CreateService().RefundOrderAsync(_buyerId, 1);

        Assert.Equal(RefundOrderOutcome.AlreadyRefunded, result.Outcome);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsNotPaid_WhenOrderAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().RefundOrderAsync(_buyerId, 1);

        Assert.Equal(RefundOrderOutcome.NotPaid, result.Outcome);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsOrderNotFound_ForAnotherShopper()
    {
        PaidOrder(); // belongs to _buyerId

        var result = await CreateService().RefundOrderAsync("someone-else", 1);

        Assert.Equal(RefundOrderOutcome.OrderNotFound, result.Outcome);
    }
}
