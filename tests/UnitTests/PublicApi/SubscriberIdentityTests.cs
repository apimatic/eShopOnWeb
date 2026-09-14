using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class SubscriberIdentityTests
{
    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    [Fact]
    public void ReturnsNullForAnUnauthenticatedCaller()
    {
        Assert.Null(SubscriberIdentity.Resolve(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(SubscriberIdentity.Resolve(null));
    }

    [Fact]
    public void ReturnsNullWhenTheTokenCarriesNoIdentity()
    {
        Assert.Null(SubscriberIdentity.Resolve(Authenticated(new Claim(ClaimTypes.Role, "ADMINISTRATORS"))));
    }

    [Fact]
    public void PrefersTheEmailClaimOverTheNameClaim()
    {
        var subscriber = SubscriberIdentity.Resolve(Authenticated(
            new Claim(ClaimTypes.Name, "legacy-username"),
            new Claim(ClaimTypes.Email, "ada@example.com")));

        Assert.NotNull(subscriber);
        Assert.Equal("ada@example.com", subscriber!.Email);
    }

    [Fact]
    public void FallsBackToTheNameClaimWhenThereIsNoEmailClaim()
    {
        var subscriber = SubscriberIdentity.Resolve(Authenticated(
            new Claim(ClaimTypes.Name, "demouser@microsoft.com")));

        Assert.NotNull(subscriber);
        Assert.Equal("demouser@microsoft.com", subscriber!.Email);
    }

    [Fact]
    public void LowerCasesTheKeySoCasingCannotSplitOneShopperIntoTwoCustomers()
    {
        var subscriber = SubscriberIdentity.Resolve(Authenticated(
            new Claim(ClaimTypes.Email, "DemoUser@Microsoft.COM")));

        Assert.Equal("demouser@microsoft.com", subscriber!.Key);
        Assert.Equal("DemoUser@Microsoft.COM", subscriber.Email);
    }

    [Fact]
    public void UsesTheGivenNameAndSurnameClaimsWhenThePresent()
    {
        var subscriber = SubscriberIdentity.Resolve(Authenticated(
            new Claim(ClaimTypes.Email, "ada@example.com"),
            new Claim(ClaimTypes.GivenName, "Ada"),
            new Claim(ClaimTypes.Surname, "Lovelace")));

        Assert.Equal("Ada", subscriber!.FirstName);
        Assert.Equal("Lovelace", subscriber.LastName);
    }

    [Fact]
    public void CallerSuppliedNamesWinOverAnythingDerived()
    {
        var subscriber = SubscriberIdentity.Resolve(
            Authenticated(new Claim(ClaimTypes.Email, "ada@example.com")),
            firstName: "Augusta",
            lastName: "King",
            organization: "Analytical Engines Ltd");

        Assert.Equal("Augusta", subscriber!.FirstName);
        Assert.Equal("King", subscriber.LastName);
        Assert.Equal("Analytical Engines Ltd", subscriber.Organization);
    }

    [Theory]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada_lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("ada-byron-lovelace@example.com", "Ada", "Byron Lovelace")]
    public void WorksANameOutOfTheEmailLocalPart(string email, string expectedFirst, string expectedLast)
    {
        var (first, last) = SubscriberIdentity.DeriveName(email, null, null);

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Fact]
    public void MarksAnAbsentSurnameAsUnspecifiedRatherThanInventingOne()
    {
        var (first, last) = SubscriberIdentity.DeriveName("demouser@microsoft.com", null, null);

        Assert.Equal("Demouser", first);
        Assert.Equal(SubscriberIdentity.UnknownNamePlaceholder, last);
    }

    [Fact]
    public void ProducesNamesTheBillingProviderWillAccept()
    {
        // Maxio rejects a blank first or last name, so the resolved subscriber must always validate.
        var subscriber = SubscriberIdentity.Resolve(Authenticated(new Claim(ClaimTypes.Email, "x@y.com")));

        subscriber!.Validate();
    }
}
