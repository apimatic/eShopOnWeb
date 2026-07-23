using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class ReadSubscriptions
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly SubscriptionService _service;

    public ReadSubscriptions()
    {
        _service = new SubscriptionService(_billingClient, Substitute.For<IPublisher>(),
            new NullAppLogger<SubscriptionService>());
    }

    [Fact]
    public async Task ListsThePlansTheBillingClientReports()
    {
        _billingClient.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { TestData.ProPlan, TestData.BasicPlan });

        var plans = await _service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
    }

    /// <summary>
    /// The user-to-subscription mapping is stateless: a user with no provider-side customer simply
    /// has no subscriptions, which must not be an error.
    /// </summary>
    [Fact]
    public async Task ReturnsNoSubscriptionsForAUserWhoHasNeverSubscribed()
    {
        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var subscriptions = await _service.GetSubscriptionsForUserAsync(TestData.BuyerId);

        Assert.Empty(subscriptions);
        await _billingClient.DidNotReceiveWithAnyArgs().ListSubscriptionsForCustomerAsync(default, default);
    }

    [Fact]
    public async Task ReturnsNoSubscriptionsForACustomerWithNoneOnFile()
    {
        ArrangeCustomerWith(Array.Empty<BillingSubscription>());

        Assert.Empty(await _service.GetSubscriptionsForUserAsync(TestData.BuyerId));
    }

    [Fact]
    public async Task AttachesTheEShopUserToEverySubscriptionItReturns()
    {
        ArrangeCustomerWith(new[] { TestData.Subscription(), TestData.Subscription(id: 777) });

        var subscriptions = await _service.GetSubscriptionsForUserAsync(TestData.BuyerId);

        Assert.Equal(2, subscriptions.Count);
        Assert.All(subscriptions, s => Assert.Equal(TestData.BuyerId, s.BuyerId));
    }

    [Fact]
    public async Task ReturnsInactiveSubscriptionsTooSoTheCustomerCanReactivateThem()
    {
        ArrangeCustomerWith(new[] { TestData.Subscription(SubscriptionState.Canceled) });

        var subscription = Assert.Single(await _service.GetSubscriptionsForUserAsync(TestData.BuyerId));

        Assert.Equal(SubscriptionState.Canceled, subscription.State);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task FindsOneOfTheUsersSubscriptionsById()
    {
        ArrangeCustomerWith(new[] { TestData.Subscription(), TestData.Subscription(id: 777) });

        var subscription = await _service.GetSubscriptionForUserAsync(TestData.BuyerId, 777);

        Assert.NotNull(subscription);
        Assert.Equal(777, subscription.ProviderSubscriptionId);
    }

    /// <summary>A subscription that is not the caller's must not be reachable through their view.</summary>
    [Fact]
    public async Task ReturnsNullForASubscriptionThatIsNotTheUsers()
    {
        ArrangeCustomerWith(new[] { TestData.Subscription() });

        Assert.Null(await _service.GetSubscriptionForUserAsync(TestData.BuyerId, 424242));
    }

    [Fact]
    public void ProjectsTheProviderRecordOntoTheDomainAggregate()
    {
        var subscription = new Subscription(TestData.BuyerId, TestData.Subscription());

        Assert.Equal(TestData.BuyerId, subscription.BuyerId);
        Assert.Equal(TestData.SubscriptionId, subscription.ProviderSubscriptionId);
        Assert.Equal(TestData.CustomerId, subscription.ProviderCustomerId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public void RefreshesTheCachedProviderViewInPlace()
    {
        var subscription = new Subscription(TestData.BuyerId, TestData.Subscription());

        subscription.RefreshFrom(TestData.Subscription(SubscriptionState.Paused, productHandle: "basic-plan"));

        Assert.Equal(SubscriptionState.Paused, subscription.State);
        Assert.Equal("basic-plan", subscription.PlanHandle);
        Assert.False(subscription.IsActive);
    }

    private void ArrangeCustomerWith(IReadOnlyCollection<BillingSubscription> subscriptions)
    {
        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns(TestData.Customer);
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(subscriptions);
    }
}
