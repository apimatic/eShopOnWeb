using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogItemTests;

public class StockTests
{
    private static CatalogItem NewItem() =>
        new(catalogTypeId: 1, catalogBrandId: 1, description: "desc", name: "name", price: 1.23m, pictureUri: "uri");

    [Fact]
    public void NewItemHasZeroStock()
    {
        Assert.Equal(0, NewItem().AvailableStock);
    }

    [Fact]
    public void SetStockSetsAbsoluteLevel()
    {
        var item = NewItem();
        item.SetStock(42);
        Assert.Equal(42, item.AvailableStock);

        item.SetStock(7);
        Assert.Equal(7, item.AvailableStock);
    }

    [Fact]
    public void SetStockAllowsZero()
    {
        var item = NewItem();
        item.SetStock(5);
        item.SetStock(0);
        Assert.Equal(0, item.AvailableStock);
    }

    [Fact]
    public void SetStockRejectsNegative()
    {
        var item = NewItem();
        Assert.ThrowsAny<System.Exception>(() => item.SetStock(-1));
    }

    [Fact]
    public void RemoveStockDecrements()
    {
        var item = NewItem();
        item.SetStock(10);
        item.RemoveStock(3);
        Assert.Equal(7, item.AvailableStock);
    }

    [Fact]
    public void RemoveStockRefusesMoreThanHeld()
    {
        var item = NewItem();
        item.SetStock(2);
        var ex = Assert.Throws<InsufficientStockException>(() => item.RemoveStock(3));
        Assert.Equal(2, ex.Available);
        Assert.Equal(3, ex.Requested);
        Assert.Equal(2, item.AvailableStock); // unchanged on refusal
    }

    [Fact]
    public void RemoveStockRejectsZeroOrNegative()
    {
        var item = NewItem();
        item.SetStock(5);
        Assert.ThrowsAny<System.Exception>(() => item.RemoveStock(0));
        Assert.ThrowsAny<System.Exception>(() => item.RemoveStock(-1));
    }
}
