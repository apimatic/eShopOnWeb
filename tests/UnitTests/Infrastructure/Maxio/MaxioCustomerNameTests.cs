using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioCustomerNameTests
{
    [Fact]
    public void PrefersTheNamesTheCallerSupplied()
    {
        var subscriber = new Subscriber("demouser@microsoft.com", "demouser@microsoft.com", "Ada", "Lovelace");

        Assert.Equal(("Ada", "Lovelace"), MaxioCustomerName.Resolve(subscriber));
    }

    [Fact]
    public void SplitsADottedEmailLocalPartIntoAGivenAndFamilyName()
    {
        var subscriber = new Subscriber("ada.lovelace@example.com", "ada.lovelace@example.com");

        Assert.Equal(("Ada", "Lovelace"), MaxioCustomerName.Resolve(subscriber));
    }

    [Fact]
    public void FallsBackToAPlaceholderFamilyNameWhenTheEmailHasNoSeparator()
    {
        var subscriber = new Subscriber("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal(("Demouser", "Customer"), MaxioCustomerName.Resolve(subscriber));
    }

    [Fact]
    public void FillsInOnlyTheNameThatIsMissing()
    {
        var subscriber = new Subscriber("ada.lovelace@example.com", "ada.lovelace@example.com", LastName: "Byron");

        Assert.Equal(("Ada", "Byron"), MaxioCustomerName.Resolve(subscriber));
    }
}
