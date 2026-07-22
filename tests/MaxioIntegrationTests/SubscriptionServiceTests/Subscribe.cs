using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class Subscribe : SubscriptionServiceFixture
{
    public Subscribe()
    {
        BillingClient.FindPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(ProPlan());
        BillingClient.EnsureCustomerAsync(UserReference, UserReference, Arg.Any<CancellationToken>())
            .Returns(Customer());
    }

    [Fact]
    public async Task EnrolsTheCustomerAndPublishesActivation()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        BillingClient.CreateSubscriptionAsync(33, "eshop-pro", Arg.Any<CancellationToken>())
            .Returns(Subscription());

        var subscription = await Service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(42, subscription.Id);
        await Publisher.Received(1).Publish(
            Arg.Is<SubscriptionActivated>(activated =>
                activated.SubscriptionId == 42
                && activated.UserReference == UserReference
                && activated.PlanHandle == "eshop-pro"
                && activated.Price == 299.00m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsuresTheProviderCustomerExistsBeforeEnrolling()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        BillingClient.CreateSubscriptionAsync(33, "eshop-pro", Arg.Any<CancellationToken>()).Returns(Subscription());

        await Service.SubscribeAsync(UserReference, "eshop-pro");

        await BillingClient.Received(1).EnsureCustomerAsync(UserReference, UserReference, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        // A double-click or a retried call must never produce a second enrolment.
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });

        var subscription = await Service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(42, subscription.Id);
        await BillingClient.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotRepublishActivationForAnAlreadyActiveSubscription()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });

        await Service.SubscribeAsync(UserReference, "eshop-pro");

        await Publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesASecondLiveSubscriptionOnADifferentPlan()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(planHandle: "basic-plan") });

        var exception = await Assert.ThrowsAsync<DuplicateSubscriptionException>(
            () => Service.SubscribeAsync(UserReference, "eshop-pro"));

        Assert.Equal(42, exception.ExistingSubscriptionId);
        Assert.Equal("basic-plan", exception.ExistingPlanHandle);
        await BillingClient.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribesWhenOnlyCancelledSubscriptionsExist()
    {
        // A cancelled subscription does not occupy the customer's live slot.
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(state: SubscriptionState.Canceled, planHandle: "basic-plan") });
        BillingClient.CreateSubscriptionAsync(33, "eshop-pro", Arg.Any<CancellationToken>()).Returns(Subscription());

        var subscription = await Service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(42, subscription.Id);
    }

    [Fact]
    public async Task RefusesToEnrolAgainstAnUnresolvablePlanHandle()
    {
        BillingClient.FindPlanByHandleAsync("ghost-plan", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.SubscribeAsync(UserReference, "ghost-plan"));

        // Nothing may be created against a guessed plan.
        await BillingClient.DidNotReceive().EnsureCustomerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LetsAProviderFailureSurfaceToTheCaller()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        BillingClient.CreateSubscriptionAsync(33, "eshop-pro", Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("CreateSubscription", 422, "Payment profile required."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => Service.SubscribeAsync(UserReference, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        await Publisher.DidNotReceive().Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenPublishingTheNotificationFails()
    {
        // Eventing is best-effort and in-process only: a failing handler must never undo billing.
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        BillingClient.CreateSubscriptionAsync(33, "eshop-pro", Arg.Any<CancellationToken>()).Returns(Subscription());
        Publisher.Publish(Arg.Any<SubscriptionActivated>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler blew up"));

        var subscription = await Service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(42, subscription.Id);
        Logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task ListsNoSubscriptionsForAUserWithNoProviderCustomer()
    {
        BillingClient.FindCustomerByReferenceAsync("nobody@example.com", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        Assert.Empty(await Service.ListMySubscriptionsAsync("nobody@example.com"));
    }
}
