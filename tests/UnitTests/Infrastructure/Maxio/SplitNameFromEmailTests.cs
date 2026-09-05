using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SplitNameFromEmailTests
{
    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("jane_doe@example.com", "Jane", "Doe")]
    [InlineData("jane@example.com", "Jane", "Customer")]
    public void DerivesAFirstAndLastNameFromTheLocalPartOfTheEmail(string email, string expectedFirst, string expectedLast)
    {
        var (firstName, lastName) = MaxioSubscriptionBillingService.SplitNameFromEmail(email);

        Assert.Equal(expectedFirst, firstName);
        Assert.Equal(expectedLast, lastName);
    }
}
