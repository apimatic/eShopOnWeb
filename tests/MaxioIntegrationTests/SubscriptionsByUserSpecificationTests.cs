using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The query specification that scopes subscriptions to one eShopOnWeb user. It must never let one
/// customer's subscriptions appear under another's account.
/// </summary>
public class SubscriptionsByUserSpecificationTests
{
    private static Subscription ForUser(int id, string userReference) =>
        new(id, userReference, 1, "eshop-pro", "Pro Plan", 299.00m, 1, "month",
            SubscriptionState.Active, null, null, false, null);

    [Fact]
    public void SelectsOnlyTheSubscriptionsBelongingToTheGivenUser()
    {
        var all = new[]
        {
            ForUser(1, "demouser@microsoft.com"),
            ForUser(2, "someone.else@microsoft.com"),
            ForUser(3, "demouser@microsoft.com")
        };

        var matched = new SubscriptionsByUserSpecification("demouser@microsoft.com").Evaluate(all).ToList();

        Assert.Equal(new[] { 1, 3 }, matched.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void SelectsNothingForAUserWithNoSubscriptions()
    {
        var all = new[] { ForUser(1, "demouser@microsoft.com") };

        Assert.Empty(new SubscriptionsByUserSpecification("nobody@microsoft.com").Evaluate(all));
    }
}
