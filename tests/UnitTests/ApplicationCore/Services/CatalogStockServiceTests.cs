using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class CatalogStockServiceTests
{
    private readonly IRepository<CatalogStockEntry> _stockRepo = Substitute.For<IRepository<CatalogStockEntry>>();
    private readonly IReadRepository<CatalogItem> _itemRepo = Substitute.For<IReadRepository<CatalogItem>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();

    public CatalogStockServiceTests()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://example/pic.png");
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());
    }

    private CatalogStockService Service() => new(_stockRepo, _itemRepo, _orderRepo, _uriComposer);

    private static CatalogItem Item(int id, decimal price = 10m)
    {
        var item = Substitute.For<CatalogItem>(1, 1, "desc", $"Name{id}", price, "pic.png");
        item.Id.Returns(id);
        return item;
    }

    [Fact]
    public async Task GetStockAsync_returns_zero_when_never_set()
    {
        _stockRepo.FirstOrDefaultAsync(Arg.Any<CatalogStockEntryByCatalogItemIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogStockEntry?)null);

        var qty = await Service().GetStockAsync(5, CancellationToken.None);

        Assert.Equal(0, qty);
    }

    [Fact]
    public async Task SetStockAsync_creates_entry_when_none_exists()
    {
        var item = Item(5);
        _itemRepo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(item);
        _stockRepo.FirstOrDefaultAsync(Arg.Any<CatalogStockEntryByCatalogItemIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((CatalogStockEntry?)null);

        await Service().SetStockAsync(5, 42, CancellationToken.None);

        await _stockRepo.Received(1).AddAsync(
            Arg.Is<CatalogStockEntry>(e => e.CatalogItemId == 5 && e.Quantity == 42), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetStockAsync_throws_when_item_missing()
    {
        _itemRepo.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((CatalogItem?)null);

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => Service().SetStockAsync(99, 1, CancellationToken.None));
    }

    [Fact]
    public async Task CreateOrderAsync_refuses_when_order_exceeds_stock()
    {
        var items = new List<CatalogItem> { Item(5) };
        var entries = new List<CatalogStockEntry> { new(5, 2) };
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>()).Returns(items);
        _stockRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var lines = new List<OrderLine> { new(5, 3) };

        await Assert.ThrowsAsync<InsufficientStockException>(
            () => Service().CreateOrderAsync("buyer@test.com", lines, CancellationToken.None));

        await _orderRepo.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrderAsync_decrements_stock_and_builds_order()
    {
        var items = new List<CatalogItem> { Item(5, price: 9.99m) };
        var entry = new CatalogStockEntry(5, 10);
        var entries = new List<CatalogStockEntry> { entry };
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>()).Returns(items);
        _stockRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var lines = new List<OrderLine> { new(5, 3) };

        var order = await Service().CreateOrderAsync("buyer@test.com", lines, CancellationToken.None);

        Assert.Equal(7, entry.Quantity); // 10 - 3
        var item = Assert.Single(order.OrderItems);
        Assert.Equal(3, item.Units);
        Assert.Equal(9.99m, item.UnitPrice);
        Assert.Equal(5, item.ItemOrdered.CatalogItemId);
        await _stockRepo.Received(1).UpdateAsync(entry, Arg.Any<CancellationToken>());
        await _orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllStockAsync_reports_zero_for_items_without_an_entry()
    {
        var items = new List<CatalogItem> { Item(1), Item(2) };
        var entries = new List<CatalogStockEntry> { new(1, 5) };
        _itemRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(items);
        _stockRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(entries);

        var levels = await Service().GetAllStockAsync(CancellationToken.None);

        Assert.Equal(2, levels.Count);
        Assert.Equal(5, levels.Single(l => l.CatalogItemId == 1).Quantity);
        Assert.Equal(0, levels.Single(l => l.CatalogItemId == 2).Quantity);
        Assert.Equal("ESHOP-2", levels.Single(l => l.CatalogItemId == 2).Sku);
    }
}
