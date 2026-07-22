using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.SubscriptionServiceTests;

public class Subscribe
{
    private const string UserName = SubscriptionBuilder.UserName;

    private readonly SubscriptionServiceBuilder _builder = new SubscriptionServiceBuilder().WithResolvablePlans();

    public Subscribe()
    {
        _builder.BillingClient.ListSubscriptionsAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());
        _builder.BillingClient.EnsureCustomerAsync(UserName, Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(88001, UserName, UserName, "demouser", "microsoft.com"));
        _builder.BillingClient.CreateSubscriptionAsync(88001, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());
    }

    [Fact]
    public async Task EnrolsTheUserAndPublishesTheActivation()
    {
        var subscription = await _builder.Build().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(299.00m, subscription.PlanPrice);

        await _builder.Publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(activated => activated.Subscription.Id == 15236915),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsesTheUserNameAsTheProviderCustomerReference()
    {
        await _builder.Build().SubscribeAsync(UserName, "eshop-pro");

        // The stable reference is what makes repeated subscribes idempotent (plan.md §4.4).
        await _builder.BillingClient.Received(1).EnsureCustomerAsync(UserName, UserName,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var existing = new SubscriptionBuilder().Build();
        _builder.BillingClient.ListSubscriptionsAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var subscription = await _builder.Build().SubscribeAsync(UserName, "eshop-pro");

        Assert.Equal(existing.Id, subscription.Id);

        // A double-click must never produce a second enrollment.
        await _builder.BillingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrolsAgainWhenTheOnlyExistingSubscriptionIsCancelled()
    {
        _builder.BillingClient.ListSubscriptionsAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new[] { new SubscriptionBuilder().InState(SubscriptionState.Canceled).Build() });

        await _builder.Build().SubscribeAsync(UserName, "eshop-pro");

        await _builder.BillingClient.Received(1).CreateSubscriptionAsync(88001, "eshop-pro",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToEnrolAgainstAnUnresolvedPlanHandle()
    {
        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _builder.Build().SubscribeAsync(UserName, "stale-handle"));

        Assert.Contains("stale-handle", exception.Message);

        // Nothing is created when the configuration does not match the sandbox.
        await _builder.BillingClient.DidNotReceive().EnsureCustomerAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LetsAProviderFailureSurfaceToTheCaller()
    {
        _builder.BillingClient.CreateSubscriptionAsync(88001, "eshop-pro", Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("CreateSubscriptionAsync", 422,
                new[] { "A payment profile is required" }));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().SubscribeAsync(UserName, "eshop-pro"));

        Assert.Contains("A payment profile is required", exception.Errors);
        await _builder.Publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenANotificationHandlerFails()
    {
        _builder.Publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the email handler blew up"));

        var subscription = await _builder.Build().SubscribeAsync(UserName, "eshop-pro");

        // Eventing is best-effort: a failed handler never undoes a successful enrollment (§2.5).
        Assert.Equal(15236915, subscription.Id);
    }
}
