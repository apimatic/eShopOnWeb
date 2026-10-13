using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.CatalogItemTests;

public class CatalogItemStock
{
    private static CatalogItem NewItem() =>
        new(catalogTypeId: 1, catalogBrandId: 1, description: "desc", name: "name", price: 1m, pictureUri: "uri");

    [Fact]
    public void DefaultsToZero()
    {
        Assert.Equal(0, NewItem().StockQuantity);
    }

    [Fact]
    public void SetStock_SetsAbsoluteQuantity()
    {
        var item = NewItem();
        item.SetStock(25);
        Assert.Equal(25, item.StockQuantity);
    }

    [Fact]
    public void RemoveStock_DeductsUnits()
    {
        var item = NewItem();
        item.SetStock(10);
        item.RemoveStock(3);
        Assert.Equal(7, item.StockQuantity);
    }

    [Fact]
    public void RemoveStock_MoreThanHeld_Throws()
    {
        var item = NewItem();
        item.SetStock(2);
        Assert.Throws<InsufficientStockException>(() => item.RemoveStock(5));
    }
}
