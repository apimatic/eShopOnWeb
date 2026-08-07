using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.BuyerTests;

public class BuyerPaymentMethods
{
    [Fact]
    public void AddPaymentMethodStoresSafeDescriptorOnly()
    {
        var buyer = new Buyer("shopper-1");

        var pm = buyer.AddPaymentMethod("VISA ending 1111", "vault-token-123", "1111", "VISA", "2030-01");

        Assert.Single(buyer.PaymentMethods);
        Assert.Equal("vault-token-123", pm.CardId);
        Assert.Equal("1111", pm.Last4);
        Assert.Equal("VISA", pm.Brand);
        Assert.Equal("2030-01", pm.ExpiryMonthYear);
    }

    [Fact]
    public void FindPaymentMethodReturnsTheOwnedCard()
    {
        var buyer = new Buyer("shopper-1");
        var pm = buyer.AddPaymentMethod("alias", "token", "1111", "VISA", "2030-01");

        Assert.Same(pm, buyer.FindPaymentMethod(pm.Id));
    }

    [Fact]
    public void RemovePaymentMethodRemovesIt()
    {
        var buyer = new Buyer("shopper-1");
        var pm = buyer.AddPaymentMethod("alias", "token", "1111", "VISA", "2030-01");

        var removed = buyer.RemovePaymentMethod(pm.Id);

        Assert.Same(pm, removed);
        Assert.Empty(buyer.PaymentMethods);
        Assert.Null(buyer.FindPaymentMethod(pm.Id));
    }
}
