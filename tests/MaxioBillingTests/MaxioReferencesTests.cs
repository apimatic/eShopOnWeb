using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

public class MaxioReferencesTests
{
    [Fact]
    public void The_same_shopper_always_gets_the_same_customer_reference()
    {
        var first = MaxioReferences.ForCustomer("eshoponweb", "demouser@microsoft.com");
        var second = MaxioReferences.ForCustomer("eshoponweb", "  DemoUser@Microsoft.COM ");

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_customer_reference_stays_readable()
    {
        var reference = MaxioReferences.ForCustomer("eshoponweb", "demouser@microsoft.com");

        Assert.StartsWith("eshoponweb-demouser-microsoft-com-", reference, StringComparison.Ordinal);
        Assert.Matches("^[a-z0-9-]+$", reference);
    }

    [Fact]
    public void Addresses_that_slug_alike_still_get_different_references()
    {
        // Both slug to "a-b-example-com"; only the digest keeps them apart.
        var dotted = MaxioReferences.ForCustomer("eshoponweb", "a.b@example.com");
        var hyphenated = MaxioReferences.ForCustomer("eshoponweb", "a-b@example.com");

        Assert.NotEqual(dotted, hyphenated);
    }

    [Fact]
    public void A_deployment_prefix_keeps_two_deployments_apart_on_one_site()
    {
        Assert.NotEqual(
            MaxioReferences.ForCustomer("staging", "demo@example.com"),
            MaxioReferences.ForCustomer("production", "demo@example.com"));
    }

    [Fact]
    public void A_subscription_reference_is_built_from_the_customer_and_the_plan()
    {
        var customer = MaxioReferences.ForCustomer("eshoponweb", "demo@example.com");

        Assert.Equal($"{customer}-eshop-pro", MaxioReferences.ForSubscription(customer, "eshop-pro"));
    }

    [Fact]
    public void An_idempotency_key_changes_the_subscription_reference_deterministically()
    {
        var customer = MaxioReferences.ForCustomer("eshoponweb", "demo@example.com");

        var keyed = MaxioReferences.ForSubscription(customer, "eshop-pro", "order-4711");
        var repeated = MaxioReferences.ForSubscription(customer, "eshop-pro", "order-4711");
        var different = MaxioReferences.ForSubscription(customer, "eshop-pro", "order-4712");

        Assert.Equal(keyed, repeated);
        Assert.NotEqual(keyed, different);
        Assert.NotEqual(keyed, MaxioReferences.ForSubscription(customer, "eshop-pro"));
    }

    [Fact]
    public void A_blank_idempotency_key_is_treated_as_no_key()
    {
        var customer = MaxioReferences.ForCustomer("eshoponweb", "demo@example.com");

        Assert.Equal(
            MaxioReferences.ForSubscription(customer, "eshop-pro"),
            MaxioReferences.ForSubscription(customer, "eshop-pro", "   "));
    }

    [Fact]
    public void A_sequenced_reference_is_the_original_with_a_suffix()
    {
        Assert.Equal("base-ref-2", MaxioReferences.WithSequence("base-ref", 2));
        Assert.Equal("base-ref-17", MaxioReferences.WithSequence("base-ref", 17));
    }

    [Fact]
    public void A_sequence_below_two_is_rejected_because_one_is_the_plain_reference()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaxioReferences.WithSequence("base-ref", 1));
    }

    [Theory]
    [InlineData("Demo User", "demo-user")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("a...b", "a-b")]
    [InlineData("!!!", "")]
    public void Slugs_collapse_runs_of_punctuation(string input, string expected)
    {
        Assert.Equal(expected, MaxioReferences.Slug(input));
    }

    [Fact]
    public void Very_long_addresses_still_produce_a_bounded_reference()
    {
        var email = new string('x', 400) + "@example.com";

        var reference = MaxioReferences.ForCustomer("eshoponweb", email);

        Assert.True(reference.Length < 80, $"Reference was {reference.Length} characters long.");
    }

    [Fact]
    public void An_empty_email_is_rejected_rather_than_producing_a_shared_reference()
    {
        Assert.Throws<ArgumentException>(() => MaxioReferences.ForCustomer("eshoponweb", "  "));
    }
}
