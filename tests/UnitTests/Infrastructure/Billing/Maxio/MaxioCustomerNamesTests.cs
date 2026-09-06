using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioCustomerNamesTests
{
    [Fact]
    public void PrefersNamesTheShopperActuallyGaveUs()
    {
        var (first, last) = MaxioCustomerNames.Resolve(
            new Subscriber("shopper@example.com", "Ada", "Lovelace"));

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Fact]
    public void TrimsSuppliedNames()
    {
        var (first, last) = MaxioCustomerNames.Resolve(
            new Subscriber("shopper@example.com", "  Ada  ", "  Lovelace "));

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Fact]
    public void FallsBackForWhicheverNameIsMissing()
    {
        var (first, last) = MaxioCustomerNames.Resolve(
            new Subscriber("ada.lovelace@example.com", "Ada", null));

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Theory]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada_lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada-lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada+lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada.byron.lovelace@example.com", "Ada", "Byron Lovelace")]
    public void SplitsANameOutOfTheEmailWhenItHasOne(string email, string expectedFirst, string expectedLast)
    {
        var (first, last) = MaxioCustomerNames.Resolve(new Subscriber(email));

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Fact]
    public void UsesAPlaceholderFamilyNameWhenTheEmailOffersOnlyOneToken()
    {
        // Advanced Billing rejects a blank last_name, and eShopOnWeb Identity stores no real name.
        var (first, last) = MaxioCustomerNames.Resolve(new Subscriber("demouser@microsoft.com"));

        Assert.Equal("Demouser", first);
        Assert.Equal("eShopOnWeb", last);
    }

    [Fact]
    public void StillProducesBothNamesForAnEmailMadeOnlyOfSeparators()
    {
        var (first, last) = MaxioCustomerNames.Resolve(new Subscriber("...@example.com"));

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(last));
    }
}
