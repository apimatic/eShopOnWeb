using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferenceFactoryTests
{
    private readonly MaxioReferenceFactory _factory = new("eshop");

    [Fact]
    public void The_same_user_always_maps_to_the_same_customer_reference()
    {
        Assert.Equal(
            "eshop:cust:demouser@microsoft.com",
            _factory.CustomerReference("demouser@microsoft.com"));
    }

    [Fact]
    public void References_carry_the_configured_prefix_so_they_cannot_collide_with_another_system()
    {
        Assert.StartsWith("acme:cust:", new MaxioReferenceFactory("acme").CustomerReference("someone@example.com"));
    }

    [Fact]
    public void The_same_idempotency_key_always_maps_to_the_same_subscription_reference()
    {
        var first = _factory.SubscriptionReference("demouser@microsoft.com", "checkout-1");
        var second = _factory.SubscriptionReference("demouser@microsoft.com", "checkout-1");

        Assert.Equal(first, second);
        Assert.Equal("eshop:sub:demouser@microsoft.com:k:checkout-1", first);
    }

    [Fact]
    public void Sequenced_references_separate_a_resubscribe_from_a_duplicate()
    {
        Assert.Equal("eshop:sub:u@x.test:eshop-pro:1", _factory.SubscriptionReference("u@x.test", "eshop-pro", 1));
        Assert.NotEqual(
            _factory.SubscriptionReference("u@x.test", "eshop-pro", 1),
            _factory.SubscriptionReference("u@x.test", "eshop-pro", 2));
    }

    [Fact]
    public void A_long_subscriber_key_is_folded_rather_than_truncated_so_it_stays_unique()
    {
        var shared = new string('a', 200);
        var first = _factory.CustomerReference(shared + "one@example.com");
        var second = _factory.CustomerReference(shared + "two@example.com");

        Assert.NotEqual(first, second);
        Assert.True(first.Length < 100, $"reference was {first.Length} characters");
    }
}
