using System.Collections.Generic;
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

public class RefundAndPlaceOrder
{
    private const string BuyerId = "12345";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IReadRepository<PaymentMethod> _pmRepo = Substitute.For<IReadRepository<PaymentMethod>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _itemRepo, _pmRepo, _uriComposer, _gateway);

    private static Address AnyAddress() => new("1 St", "City", "ST", "US", "00000");

    [Fact]
    public async Task RefundMarksPaidOrderRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PPO-1", "CAP-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _gateway.RefundCaptureAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundOutcome("REF-1", "COMPLETED"));

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        Assert.Equal("REF-1", result.PaymentRefundId);
        await _gateway.Received(1).RefundCaptureAsync("CAP-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundIsIdempotentForAlreadyRefundedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAsPaid("PPO-1", "CAP-1");
        order.MarkAsRefunded("REF-1");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(OrderPaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundUnpaidOrderThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<InvalidPaymentOperationException>(() => CreateService().RefundOrderAsync(BuyerId, 1));
    }

    [Fact]
    public async Task RefundUnknownOrderThrowsNotFound()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderForBuyerWithItemsSpec>(), Arg.Any<CancellationToken>()).Returns((Order?)null);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => CreateService().RefundOrderAsync(BuyerId, 1));
    }

    [Fact]
    public async Task PlaceOrderRejectsEmptyBasket()
    {
        await Assert.ThrowsAsync<PaymentInputException>(() =>
            CreateService().PlaceOrderAsync(BuyerId, new List<OrderLine>(), AnyAddress()));
    }

    [Fact]
    public async Task PlaceOrderRejectsUnknownCatalogItem()
    {
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>()); // nothing found for the requested id

        await Assert.ThrowsAsync<PaymentInputException>(() =>
            CreateService().PlaceOrderAsync(BuyerId, new List<OrderLine> { new(42, 1) }, AnyAddress()));
    }

    [Fact]
    public async Task PlaceOrderUsesCatalogPricesAndPersists()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "Widget", 9.99m, "pic.png");
        SetId(catalogItem, 5);
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://img/pic.png");
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => (Order)ci[0]);

        var order = await CreateService().PlaceOrderAsync(BuyerId, new List<OrderLine> { new(5, 3) }, AnyAddress());

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(9.99m * 3, order.Total());
        await _orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    // BaseEntity.Id has a protected setter; set the backing field for test fixtures.
    private static void SetId(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity entity, int id) =>
        typeof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity)
            .GetField("<Id>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(entity, id);
}
