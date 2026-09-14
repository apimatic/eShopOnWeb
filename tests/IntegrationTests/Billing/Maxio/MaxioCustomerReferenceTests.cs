#nullable enable
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class MaxioCustomerReferenceTests
{
    private const string Prefix = "eshoponweb";

    [Fact]
    public void IsDerivedFromTheUserNameAndIsStableAcrossCalls()
    {
        var first = MaxioCustomerReference.For("demouser@microsoft.com", Prefix);
        var second = MaxioCustomerReference.For("demouser@microsoft.com", Prefix);

        Assert.Equal("eshoponweb-demouser@microsoft.com", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IgnoresCasingAndSurroundingWhitespace()
    {
        Assert.Equal(MaxioCustomerReference.For("demouser@microsoft.com", Prefix),
            MaxioCustomerReference.For("  DemoUser@Microsoft.com  ", Prefix));
    }

    [Fact]
    public void ReplacesCharactersThatWouldNotTravelSafely()
    {
        Assert.Equal("eshoponweb-a-b-c", MaxioCustomerReference.For("a b/c", Prefix));
    }

    [Fact]
    public void CapsLengthWithoutLettingDistinctUsersCollide()
    {
        var first = MaxioCustomerReference.For(new string('a', 300) + "one", Prefix);
        var second = MaxioCustomerReference.For(new string('a', 300) + "two", Prefix);

        Assert.True(first.Length <= 100);
        Assert.True(second.Length <= 100);
        Assert.NotEqual(first, second);
    }
}
