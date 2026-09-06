using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Endpoints;

public class SubscriberProfileResolverTests
{
    [Theory]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("grace_hopper@example.com", "Grace", "Hopper")]
    [InlineData("jean-luc-picard@example.com", "Jean", "Luc Picard")]
    [InlineData("ada.lovelace+billing@example.com", "Ada", "Lovelace")]
    [InlineData("ADA.LOVELACE@EXAMPLE.COM", "Ada", "Lovelace")]
    public void Derives_a_billing_name_from_the_email_address(string email, string firstName, string lastName)
    {
        var (derivedFirst, derivedLast) = SubscriberProfileResolver.DeriveName(email);

        Assert.Equal(firstName, derivedFirst);
        Assert.Equal(lastName, derivedLast);
    }

    [Fact]
    public void Always_produces_both_names_because_the_provider_requires_both()
    {
        var (firstName, lastName) = SubscriberProfileResolver.DeriveName("---@example.com");

        Assert.False(string.IsNullOrWhiteSpace(firstName));
        Assert.False(string.IsNullOrWhiteSpace(lastName));
    }
}
