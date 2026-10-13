using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogItemTests;

public class CatalogItemStock
{
    private static CatalogItem NewItem() =>
        new(catalogTypeId: 1, catalogBrandId: 1, description: "desc", name: "name", price: 9.99m, pictureUri: "pic");

    [Fact]
    public void DefaultsToZeroStock()
    {
        var item = NewItem();
        Assert.Equal(0, item.StockQuantity);
    }

    [Fact]
    public void SetStockSetsAbsoluteQuantity()
    {
        var item = NewItem();
        item.SetStock(42);
        Assert.Equal(42, item.StockQuantity);

        item.SetStock(5);
        Assert.Equal(5, item.StockQuantity);
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
        Assert.Equal(7, item.StockQuantity);
    }

    [Fact]
    public void RemoveStockThrowsWhenInsufficient()
    {
        var item = NewItem();
        item.SetStock(2);

        var ex = Assert.Throws<InsufficientStockException>(() => item.RemoveStock(3));
        Assert.Equal(2, ex.Available);
        Assert.Equal(3, ex.Requested);
        Assert.Equal(2, item.StockQuantity); // unchanged
    }

    [Fact]
    public void RemoveExactlyAvailableLeavesZero()
    {
        var item = NewItem();
        item.SetStock(4);
        item.RemoveStock(4);
        Assert.Equal(0, item.StockQuantity);
    }
}
