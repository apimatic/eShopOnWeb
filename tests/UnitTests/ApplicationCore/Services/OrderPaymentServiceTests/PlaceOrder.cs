using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PlaceOrder
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

    private CatalogItem CatalogItem(int id, decimal price)
    {
        var item = new CatalogItem(1, 1, "desc", $"item-{id}", price, "pic.png");
        // Id has a protected setter; invoke it via reflection to simulate a persisted catalog item.
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.GetSetMethod(nonPublic: true)!.Invoke(item, new object[] { id });
        return item;
    }

    [Fact]
    public async Task PlacesOrderAwaitingPayment_UsingCatalogPrices()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://x/pic.png");
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { CatalogItem(5, 8.5m), CatalogItem(3, 12m) });
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());

        var result = await CreateService().PlaceOrderAsync(_buyerId, new[]
        {
            new OrderLineInput(5, 2),
            new OrderLineInput(3, 1)
        });

        Assert.Equal(PlaceOrderOutcome.Placed, result.Outcome);
        Assert.Equal(PaymentStatus.AwaitingPayment, result.Order!.PaymentStatus);
        Assert.Equal(29m, result.Order!.Total()); // 2*8.5 + 1*12
        await _orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsEmptyOrder_WhenNoValidLines()
    {
        var result = await CreateService().PlaceOrderAsync(_buyerId, new[] { new OrderLineInput(5, 0) });

        Assert.Equal(PlaceOrderOutcome.EmptyOrder, result.Outcome);
    }

    [Fact]
    public async Task ReturnsCatalogItemNotFound_WhenItemMissing()
    {
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>());

        var result = await CreateService().PlaceOrderAsync(_buyerId, new[] { new OrderLineInput(999, 1) });

        Assert.Equal(PlaceOrderOutcome.CatalogItemNotFound, result.Outcome);
    }
}
