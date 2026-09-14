using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class CustomerNamesTests
{
    [Fact]
    public void SplitsAMultiPartLocalPartIntoGivenAndFamilyNames()
    {
        var (first, last) = CustomerNames.Resolve("jane.doe@example.com");

        Assert.Equal("Jane", first);
        Assert.Equal("Doe", last);
    }

    [Fact]
    public void FallsBackForASingleTokenLocalPart()
    {
        var (first, last) = CustomerNames.Resolve("demouser@microsoft.com");

        Assert.Equal("Demouser", first);
        Assert.Equal(CustomerNames.FallbackLastName, last);
    }

    [Fact]
    public void PrefersExplicitlySuppliedNames()
    {
        var (first, last) = CustomerNames.Resolve("jane.doe@example.com", "Ada", "Lovelace");

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Fact]
    public void NeverReturnsAnEmptyName()
    {
        var (first, last) = CustomerNames.Resolve(string.Empty);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(last));
    }
}
