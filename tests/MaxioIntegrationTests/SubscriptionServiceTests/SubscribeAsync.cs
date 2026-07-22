using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>UC1 — the hero flow.</summary>
public class SubscribeAsync
{
    private const string UserReference = "demouser@microsoft.com";

    private static (SubscriptionService Service, FakeBillingClient Billing, RecordingPublisher Publisher) Build()
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(new BillingPlan(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month"));
        billing.Plans.Add(new BillingPlan(2, "basic-plan", "Basic Plan", 29.00m, 1, "month"));

        var publisher = new RecordingPublisher();
        var service = new SubscriptionService(billing, publisher, new RecordingLogger<SubscriptionService>());

        return (service, billing, publisher);
    }

    [Fact]
    public async Task CreatesTheProviderCustomerAndEnrolsThemInTheChosenPlan()
    {
        var (service, billing, _) = Build();

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal("eshop-pro", subscription.Plan.Handle);
        Assert.Equal(299.00m, subscription.Plan.Price);
        Assert.Equal(UserReference, subscription.UserReference);

        Assert.Contains($"CreateCustomer:{UserReference}:Demouser:eShopOnWeb", billing.Calls);
        Assert.Contains(billing.Calls, c => c.StartsWith("CreateSubscription:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReusesAnExistingProviderCustomerRatherThanCreatingASecond()
    {
        var (service, billing, _) = Build();
        billing.Customer = new BillingCustomer(90210, UserReference, UserReference);

        await service.SubscribeAsync(UserReference, "eshop-pro");

        // The lookup on the user reference is what makes repeat enrolment idempotent (plan.md §4.4).
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateCustomer:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var (service, billing, publisher) = Build();
        billing.Customer = new BillingCustomer(90210, UserReference, UserReference);

        var first = await service.SubscribeAsync(UserReference, "eshop-pro");
        billing.Calls.Clear();
        publisher.Published.Clear();

        var second = await service.SubscribeAsync(UserReference, "eshop-pro");

        // A double-click must not produce two subscriptions, and must not announce a second
        // activation for a subscription that was already active.
        Assert.Equal(first.Id, second.Id);
        Assert.Single(billing.Subscriptions);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateSubscription:", StringComparison.Ordinal));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task DoesNotEnrolTwiceWhenTheCustomerPicksADifferentPlanWhileActive()
    {
        var (service, billing, _) = Build();
        billing.Customer = new BillingCustomer(90210, UserReference, UserReference);
        await service.SubscribeAsync(UserReference, "eshop-pro");

        var second = await service.SubscribeAsync(UserReference, "basic-plan");

        // Moving between plans is a plan change (UC3), never a second enrolment.
        Assert.Single(billing.Subscriptions);
        Assert.Equal("eshop-pro", second.Plan.Handle);
    }

    [Fact]
    public async Task EnrolsAgainWhenThePreviousSubscriptionIsNoLongerActive()
    {
        var (service, billing, _) = Build();
        var customer = new BillingCustomer(90210, UserReference, UserReference);
        billing.Customer = customer;
        billing.Subscriptions.Add(new Subscription(1, UserReference, 90210, billing.Plans[0],
            SubscriptionState.Canceled, "canceled"));

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        Assert.Equal(2, billing.Subscriptions.Count);
        Assert.Equal(SubscriptionState.Active, subscription.State);
    }

    [Fact]
    public async Task PublishesSubscriptionActivatedOnASuccessfulEnrolment()
    {
        var (service, _, publisher) = Build();

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        var notification = publisher.Single<SubscriptionActivated>();
        Assert.Equal(subscription.Id, notification.Subscription.Id);
    }

    [Fact]
    public async Task KeepsTheSubscriptionWhenNotificationDeliveryFails()
    {
        var (service, billing, publisher) = Build();
        publisher.Failure = new InvalidOperationException("a handler blew up");

        var subscription = await service.SubscribeAsync(UserReference, "eshop-pro");

        // Eventing is best-effort: the enrolment already succeeded at the provider and must stand.
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Single(billing.Subscriptions);
    }

    [Fact]
    public async Task RefusesAPlanHandleThatIsNotInTheConfiguredProductFamily()
    {
        var (service, billing, _) = Build();

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.SubscribeAsync(UserReference, "some-other-product"));

        Assert.Contains("some-other-product", exception.Message);
        // Resolving against the family list is also the authorization check: nothing was created.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateSubscription:", StringComparison.Ordinal));
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("CreateCustomer:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnEmptyUserReferenceBeforeCallingTheProvider()
    {
        var (service, billing, _) = Build();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SubscribeAsync("  ", "eshop-pro"));

        Assert.Empty(billing.Calls);
    }

    [Fact]
    public async Task DerivesAFirstAndLastNameFromADottedUsername()
    {
        var (service, billing, _) = Build();

        await service.SubscribeAsync("ada.lovelace@example.com", "eshop-pro");

        // Maxio requires both names; eShopOnWeb only holds the username, so something readable is
        // derived rather than sending blanks.
        Assert.Contains("CreateCustomer:ada.lovelace@example.com:Ada:Lovelace", billing.Calls);
    }
}
