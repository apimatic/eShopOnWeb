using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferenceTests
{
    [Theory]
    [InlineData("eshop-", "demouser@microsoft.com", "eshop-demouser@microsoft.com")]
    [InlineData("eshop-", "Jane.Doe@Contoso.COM", "eshop-jane.doe@contoso.com")]
    [InlineData("", "demouser@microsoft.com", "demouser@microsoft.com")]
    [InlineData("eshop", "demouser@microsoft.com", "eshop-demouser@microsoft.com")]
    public void BuildsAReadableReferenceFromThePrefixAndUserId(string prefix, string userId, string expected)
    {
        Assert.Equal(expected, MaxioReference.ForCustomer(prefix, userId));
    }

    [Fact]
    public void IsStableAcrossCallsSoTheSameUserAlwaysResolvesToTheSameCustomer()
    {
        var first = MaxioReference.ForCustomer("eshop-", " DemoUser@Microsoft.com ");
        var second = MaxioReference.ForCustomer("eshop-", "demouser@microsoft.com");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CollapsesCharactersThatWouldNeedEscaping()
    {
        Assert.Equal("eshop-a-b-c", MaxioReference.ForCustomer("eshop-", "a b/c"));
    }

    [Fact]
    public void CapsTheLengthSoTheReferenceStaysAcceptable()
    {
        var reference = MaxioReference.ForCustomer("eshop-", new string('a', 500));

        Assert.Equal(100, reference.Length);
    }

    [Fact]
    public void RejectsAnIdentityWithNoUsableCharacters()
    {
        Assert.Throws<ArgumentException>(() => MaxioReference.ForCustomer("eshop-", "///"));
    }
}
