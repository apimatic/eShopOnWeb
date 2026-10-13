using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogItemTests;

public class CatalogItemStock
{
    private static CatalogItem NewItem() =>
        new(catalogTypeId: 1, catalogBrandId: 1, description: "d", name: "n", price: 1.23m, pictureUri: "p");

    [Fact]
    public void DefaultsToZero()
    {
        Assert.Equal(0, NewItem().StockQuantity);
    }

    [Fact]
    public void SetStock_SetsAbsoluteQuantity()
    {
        var item = NewItem();
        item.SetStock(10);
        Assert.Equal(10, item.StockQuantity);
        item.SetStock(3);
        Assert.Equal(3, item.StockQuantity);
    }

    [Fact]
    public void SetStock_RejectsNegative()
    {
        Assert.ThrowsAny<System.Exception>(() => NewItem().SetStock(-1));
    }

    [Fact]
    public void RemoveStock_DecrementsWhenSufficient()
    {
        var item = NewItem();
        item.SetStock(10);
        item.RemoveStock(4);
        Assert.Equal(6, item.StockQuantity);
    }

    [Fact]
    public void RemoveStock_ThrowsWhenInsufficient()
    {
        var item = NewItem();
        item.SetStock(2);
        var ex = Assert.Throws<InsufficientStockException>(() => item.RemoveStock(5));
        Assert.Equal(2, ex.Available);
        Assert.Equal(5, ex.Requested);
        Assert.Equal(2, item.StockQuantity); // unchanged on failure
    }
}
