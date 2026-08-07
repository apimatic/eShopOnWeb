using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.BuyerTests;

public class BuyerPaymentMethods
{
    private readonly Buyer _buyer = new("demouser@microsoft.com");

    [Fact]
    public void AddPaymentMethodStoresOnlySafeSummary()
    {
        var pm = _buyer.AddPaymentMethod("VAULT-123", "VISA", "1111", "2027-11", "Personal");

        Assert.Single(_buyer.PaymentMethods);
        Assert.Equal("VAULT-123", pm.VaultId);
        Assert.Equal("VISA", pm.CardBrand);
        Assert.Equal("1111", pm.Last4);
        Assert.Equal("2027-11", pm.Expiry);
        Assert.Equal("Personal", pm.Alias);
    }

    [Fact]
    public void GetPaymentMethodReturnsNullForUnknownId()
    {
        _buyer.AddPaymentMethod("VAULT-123", "VISA", "1111", "2027-11", null);

        Assert.Null(_buyer.GetPaymentMethod(999));
    }

    [Fact]
    public void RemovePaymentMethodRemovesIt()
    {
        var pm = _buyer.AddPaymentMethod("VAULT-123", "VISA", "1111", "2027-11", null);

        var removed = _buyer.RemovePaymentMethod(pm.Id);

        Assert.True(removed);
        Assert.Empty(_buyer.PaymentMethods);
    }

    [Fact]
    public void RemoveUnknownPaymentMethodReturnsFalse()
    {
        Assert.False(_buyer.RemovePaymentMethod(999));
    }
}
