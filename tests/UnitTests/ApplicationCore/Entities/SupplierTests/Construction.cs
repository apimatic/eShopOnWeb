using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.SupplierTests;

public class Construction
{
    [Fact]
    public void SetsNameUrlAndCreatedAt()
    {
        var supplier = new Supplier("Thistlewood", "https://supplier.example/catalog");

        Assert.Equal("Thistlewood", supplier.Name);
        Assert.Equal("https://supplier.example/catalog", supplier.ProductListingUrl);
        Assert.NotEqual(default, supplier.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://supplier.example/catalog")]
    public void RejectsInvalidListingUrl(string url)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Supplier("Thistlewood", url));
    }

    [Fact]
    public void RejectsMissingName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Supplier("", "https://supplier.example/"));
    }
}
