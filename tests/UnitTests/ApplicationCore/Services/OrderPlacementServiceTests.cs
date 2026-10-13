using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderPlacementServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private static readonly Address Ship = new("s", "c", "st", "co", "z");

    public OrderPlacementServiceTests()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://img/x.png");
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<Order>()));
    }

    private OrderPlacementService NewService(params CatalogItem[] items)
    {
        _itemRepo.ListAsync(Arg.Any<ISpecification<CatalogItem>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>(items));
        return new OrderPlacementService(_orderRepo, _itemRepo, _uriComposer);
    }

    private static CatalogItem ItemWithStock(int stock, int id = 1)
    {
        var item = new CatalogItem(1, 1, "desc", "name", 5m, "uri");
        item.SetStock(stock);
        // Id is EF-assigned in the app; set it here so the ordered-item snapshot (which requires a
        // positive catalog item id) can be built.
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(item, id);
        return item;
    }

    [Fact]
    public async Task DecrementsStockAndAddsOrder()
    {
        var item = ItemWithStock(10);
        var service = NewService(item);

        await service.PlaceOrderAsync("buyer@x.com", Ship, new[] { (item.Id, 3) });

        Assert.Equal(7, item.StockQuantity);
        await _orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesOrderBeyondStockAndLeavesEverythingUntouched()
    {
        var item = ItemWithStock(2);
        var service = NewService(item);

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            service.PlaceOrderAsync("buyer@x.com", Ship, new[] { (item.Id, 5) }));

        Assert.Equal(2, item.StockQuantity);
        await _orderRepo.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnknownCatalogItem()
    {
        var service = NewService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.PlaceOrderAsync("buyer@x.com", Ship, new[] { (999, 1) }));
    }
}
