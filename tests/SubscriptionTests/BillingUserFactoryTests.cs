using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.SubscriptionTests;

public class BillingUserFactoryTests
{
    private static ClaimsPrincipal PrincipalWithName(string name) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name) }, "test"));

    [Fact]
    public void Uses_lowercased_email_as_the_stable_reference()
    {
        var user = BillingUserFactory.FromPrincipal(PrincipalWithName("DemoUser@Microsoft.com"));

        Assert.NotNull(user);
        Assert.Equal("demouser@microsoft.com", user!.Reference);
        Assert.Equal("DemoUser@Microsoft.com", user.Email);
    }

    [Fact]
    public void Derives_a_display_name_from_the_email()
    {
        var user = BillingUserFactory.FromPrincipal(PrincipalWithName("demouser@microsoft.com"));

        Assert.Equal("demouser", user!.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(user.LastName));
    }

    [Fact]
    public void Returns_null_for_an_anonymous_principal()
    {
        Assert.Null(BillingUserFactory.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(BillingUserFactory.FromPrincipal(null));
    }
}
