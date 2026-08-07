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

public class RefundOrder
{
    private const string BuyerId = "12345";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<PaymentMethod> _pmRepo = Substitute.For<IReadRepository<PaymentMethod>>();
    private readonly IPayPalPaymentGateway _gateway = Substitute.For<IPayPalPaymentGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _gateway, _uriComposer);

    private static Order PaidOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkPaid("PPO-1", "CAP-1", "VISA ending in 1111");
        return order;
    }

    [Fact]
    public async Task RefundsAndMarksRefundedWhenPaid()
    {
        var order = PaidOrder();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.RefundAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new RefundReceipt("REF-1"));

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        Assert.Equal("REF-1", result.PayPalRefundId);
        await _gateway.Received(1).RefundAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotRefundAgainWhenAlreadyRefunded()
    {
        var order = PaidOrder();
        order.MarkRefunded("REF-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenOrderNotPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<PaymentException>(() => CreateService().RefundOrderAsync(BuyerId, 1));
        await _gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenOrderNotFound()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdAndBuyerSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => CreateService().RefundOrderAsync(BuyerId, 99));
    }
}
