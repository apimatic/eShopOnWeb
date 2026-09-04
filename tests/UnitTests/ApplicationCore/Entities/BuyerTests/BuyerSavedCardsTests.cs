using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.BuyerTests;

public class BuyerSavedCardsTests
{
    [Fact]
    public void SaveCard_KeepsOnlyVaultTokenAndSafeDescription()
    {
        var buyer = new Buyer("shopper@example.com");
        var method = buyer.AddPaymentMethod("Visa personal", "VAULT-123", "VISA", "1111", "2029-12");

        Assert.Single(buyer.PaymentMethods);
        Assert.Equal("VAULT-123", method.VaultId);
        Assert.Equal("1111", method.Last4);
        // CardId mirrors the vault token, never a card number.
        Assert.Equal("VAULT-123", method.CardId);
    }

    [Fact]
    public void RemoveCard_DeletesIt()
    {
        var buyer = new Buyer("shopper@example.com");
        var method = buyer.AddPaymentMethod(null, "VAULT-9", "VISA", "1111", "2030-01");

        Assert.True(buyer.RemovePaymentMethod(method.Id));
        Assert.Empty(buyer.PaymentMethods);
        Assert.False(buyer.RemovePaymentMethod(method.Id));
    }

    [Fact]
    public void PayPalSettings_BaseUrlOverrideWinsForEveryCall()
    {
        var settings = new PayPalSettings { Environment = "live", BaseUrl = "https://gateway.example.test/" };
        Assert.Equal("https://gateway.example.test", settings.ResolvedBaseUrl);
    }

    [Fact]
    public void PayPalSettings_EnvironmentDerivesSandboxAndLive()
    {
        Assert.Equal("https://api-m.sandbox.paypal.com", new PayPalSettings { Environment = "sandbox" }.ResolvedBaseUrl);
        Assert.Equal("https://api-m.paypal.com", new PayPalSettings { Environment = "live" }.ResolvedBaseUrl);
        Assert.Equal("https://api-m.sandbox.paypal.com", new PayPalSettings().ResolvedBaseUrl);
    }
}
