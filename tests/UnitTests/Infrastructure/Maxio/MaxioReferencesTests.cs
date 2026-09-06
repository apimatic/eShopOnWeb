using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioReferencesTests
{
    private readonly MaxioReferences _references = new("eshoponweb");

    private static Subscriber Demo(string userName = "demouser@microsoft.com") =>
        new(userId: "user-1", userName: userName, email: userName);

    [Fact]
    public void DerivesTheCustomerReferenceFromTheUserName()
    {
        Assert.Equal("eshoponweb-demouser@microsoft.com", _references.ForCustomer(Demo()));
    }

    [Fact]
    public void NormalisesCasingAndPaddingSoTheReferenceIsStable()
    {
        // Maxio treats customer references case-insensitively, so the same user must always map to
        // one reference no matter how the user name was typed.
        Assert.Equal(
            _references.ForCustomer(Demo("demouser@microsoft.com")),
            _references.ForCustomer(Demo("  DemoUser@Microsoft.COM  ")));
    }

    [Fact]
    public void FallsBackToADigestForImplausiblyLongUserNames()
    {
        var longName = new string('a', 200) + "@example.com";

        var reference = _references.ForCustomer(Demo(longName));

        Assert.Equal("eshoponweb-".Length + 32, reference.Length);
        Assert.StartsWith("eshoponweb-", reference);

        // Still deterministic: the same user resolves to the same reference every time.
        Assert.Equal(reference, _references.ForCustomer(Demo(longName)));
    }

    [Fact]
    public void UsesTheConfiguredPrefix()
    {
        Assert.StartsWith("tenant-b-", new MaxioReferences("tenant-b").ForCustomer(Demo()));
    }

    [Fact]
    public void ScopesTheSubscriptionReferenceToTheCustomerAndPlan()
    {
        var customerReference = _references.ForCustomer(Demo());

        Assert.Equal(
            "eshoponweb-demouser@microsoft.com:eshop-pro",
            _references.ForSubscription(customerReference, "eshop-pro"));

        Assert.NotEqual(
            _references.ForSubscription(customerReference, "eshop-pro"),
            _references.ForSubscription(customerReference, "basic-plan"));
    }

    [Fact]
    public void MintsADistinctReferenceWhenResubscribing()
    {
        var customerReference = _references.ForCustomer(Demo());

        var resubscription = _references.ForResubscription(customerReference, "eshop-pro");

        Assert.StartsWith(_references.ForSubscription(customerReference, "eshop-pro") + ":", resubscription);
        Assert.NotEqual(_references.ForSubscription(customerReference, "eshop-pro"), resubscription);
    }
}
