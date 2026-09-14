using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class SubscriberFactoryTests
{
    [Fact]
    public void TakesTheIdentityFromTheTokenName()
    {
        Assert.True(SubscriberFactory.TryCreate(Principal("demouser@microsoft.com"), out var subscriber, out _));

        Assert.Equal("demouser@microsoft.com", subscriber.ExternalId);
        Assert.Equal("demouser@microsoft.com", subscriber.Email);
        Assert.Equal(SubscriberFactory.OrganizationName, subscriber.Organization);
    }

    [Theory]
    [InlineData("jane.doe@contoso.com", "Jane", "Doe")]
    [InlineData("jane.van.doe@contoso.com", "Jane", "Van Doe")]
    [InlineData("jane_doe@contoso.com", "Jane", "Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Microsoft")]
    public void DerivesTheNamePairTheBillingSystemRequires(string email, string firstName, string lastName)
    {
        Assert.True(SubscriberFactory.TryCreate(Principal(email), out var subscriber, out _));

        Assert.Equal(firstName, subscriber.FirstName);
        Assert.Equal(lastName, subscriber.LastName);
    }

    [Fact]
    public void FallsBackToTheEmailClaimWhenTheUserNameIsNotAnAddress()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "demouser"),
                new Claim(ClaimTypes.Email, "demouser@microsoft.com")
            },
            authenticationType: "Test"));

        Assert.True(SubscriberFactory.TryCreate(principal, out var subscriber, out _));

        Assert.Equal("demouser", subscriber.ExternalId);
        Assert.Equal("demouser@microsoft.com", subscriber.Email);
    }

    [Fact]
    public void ExplainsWhyATokenWithoutAnEmailCannotSubscribe()
    {
        Assert.False(SubscriberFactory.TryCreate(Principal("demouser"), out _, out var error));

        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnAnonymousCaller()
    {
        Assert.False(SubscriberFactory.TryCreate(new ClaimsPrincipal(new ClaimsIdentity()), out _, out var error));

        Assert.Contains("user name", error, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal Principal(string userName) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }, authenticationType: "Test"));
}
