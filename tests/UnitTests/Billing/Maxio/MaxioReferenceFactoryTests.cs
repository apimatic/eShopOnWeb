using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

public class MaxioReferenceFactoryTests
{
    [Fact]
    public void Customer_reference_is_namespaced_and_derived_from_the_user_name()
    {
        var reference = MaxioReferenceFactory.ForCustomer(new SubscriberIdentity("demouser@microsoft.com"));

        Assert.Equal("eshoponweb:demouser@microsoft.com", reference);
    }

    [Fact]
    public void Customer_reference_ignores_the_casing_of_the_user_name()
    {
        Assert.Equal(
            MaxioReferenceFactory.ForCustomer(new SubscriberIdentity("DemoUser@Microsoft.com")),
            MaxioReferenceFactory.ForCustomer(new SubscriberIdentity("demouser@microsoft.com")));
    }

    [Fact]
    public void Customer_reference_does_not_depend_on_the_email_or_name()
    {
        Assert.Equal(
            MaxioReferenceFactory.ForCustomer(new SubscriberIdentity("demouser@microsoft.com", "other@example.com", "A", "B")),
            MaxioReferenceFactory.ForCustomer(new SubscriberIdentity("demouser@microsoft.com")));
    }

    [Fact]
    public void Subscription_reference_scopes_the_customer_reference_by_plan()
    {
        var reference = MaxioReferenceFactory.ForSubscription(new SubscriberIdentity("demouser@microsoft.com"), "eshop-pro");

        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro", reference);
    }

    [Fact]
    public void Subscription_references_differ_per_plan()
    {
        var subscriber = new SubscriberIdentity("demouser@microsoft.com");

        Assert.NotEqual(
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro"),
            MaxioReferenceFactory.ForSubscription(subscriber, "basic-plan"));
    }

    [Fact]
    public void An_idempotency_key_opens_a_distinct_scope()
    {
        var subscriber = new SubscriberIdentity("demouser@microsoft.com");

        Assert.Equal("eshoponweb:demouser@microsoft.com:eshop-pro:renewal-2026-09",
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro", "renewal-2026-09"));
        Assert.NotEqual(
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro"),
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro", "renewal-2026-09"));
    }

    [Fact]
    public void A_blank_idempotency_key_is_the_same_as_none()
    {
        var subscriber = new SubscriberIdentity("demouser@microsoft.com");

        Assert.Equal(
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro"),
            MaxioReferenceFactory.ForSubscription(subscriber, "eshop-pro", "   "));
    }

    [Fact]
    public void An_over_long_reference_stays_bounded_deterministic_and_distinct()
    {
        var first = new SubscriberIdentity(new string('a', 300) + "1@example.com");
        var second = new SubscriberIdentity(new string('a', 300) + "2@example.com");

        var reference = MaxioReferenceFactory.ForSubscription(first, "eshop-pro");

        Assert.True(reference.Length <= 200);
        Assert.Equal(reference, MaxioReferenceFactory.ForSubscription(first, "eshop-pro"));
        Assert.NotEqual(reference, MaxioReferenceFactory.ForSubscription(second, "eshop-pro"));
    }
}
