using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriberIdentityFactoryTest
{
    [TestMethod]
    public void UsesNameClaimAsStableReference()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "demouser@microsoft.com") }, authenticationType: "Test"));

        var identity = SubscriberIdentityFactory.FromPrincipal(principal);

        // The stable reference (and email) is the user's login — this is what keeps customer-ensure idempotent.
        Assert.AreEqual("demouser@microsoft.com", identity.Reference);
        Assert.AreEqual("demouser@microsoft.com", identity.Email);
        Assert.IsFalse(string.IsNullOrWhiteSpace(identity.FirstName));
        Assert.IsFalse(string.IsNullOrWhiteSpace(identity.LastName));
    }

    [TestMethod]
    [ExpectedException(typeof(SubscriptionBillingException))]
    public void ThrowsWhenNoNameClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        SubscriberIdentityFactory.FromPrincipal(principal);
    }
}
