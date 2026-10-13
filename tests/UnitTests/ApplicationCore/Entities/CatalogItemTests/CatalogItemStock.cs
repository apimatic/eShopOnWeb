using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogItemTests;

public class CatalogItemStock
{
    private static CatalogItem NewItem() =>
        new(1, 1, "desc", "name", 9.99m, "uri");

    [Fact]
    public void DefaultsToZero()
    {
        Assert.Equal(0, NewItem().StockQuantity);
    }

    [Fact]
    public void SetStockSetsAbsoluteLevel()
    {
        var item = NewItem();
        item.SetStock(25);
        Assert.Equal(25, item.StockQuantity);
        item.SetStock(5);
        Assert.Equal(5, item.StockQuantity);
    }

    [Fact]
    public void RemoveStockTakesUnitsOff()
    {
        var item = NewItem();
        item.SetStock(10);
        item.RemoveStock(3);
        Assert.Equal(7, item.StockQuantity);
    }

    [Fact]
    public void RemoveMoreThanHeldThrowsAndLeavesLevelUnchanged()
    {
        var item = NewItem();
        item.SetStock(2);
        var ex = Assert.Throws<InsufficientStockException>(() => item.RemoveStock(5));
        Assert.Equal(2, ex.Available);
        Assert.Equal(5, ex.Requested);
        Assert.Equal(2, item.StockQuantity);
    }
}
