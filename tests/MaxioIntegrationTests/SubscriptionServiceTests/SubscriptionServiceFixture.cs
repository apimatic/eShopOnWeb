using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>
/// Shared setup for the orchestration tests. These exercise the provider-agnostic seam: the rules
/// the service enforces before, around and after a provider call, independent of Maxio.
/// </summary>
public abstract class SubscriptionServiceFixture
{
    protected const string UserReference = "demouser@microsoft.com";
    protected const string OtherUserReference = "someoneelse@microsoft.com";

    protected readonly IBillingClient BillingClient = Substitute.For<IBillingClient>();
    protected readonly IPublisher Publisher = Substitute.For<IPublisher>();
    protected readonly IAppLogger<SubscriptionService> Logger = Substitute.For<IAppLogger<SubscriptionService>>();

    protected SubscriptionService Service => new(BillingClient, Publisher, Logger);

    protected static SubscriptionPlan ProPlan() => new(7126957, "eshop-pro", "Pro Plan", 299.00m, 1, BillingIntervalUnit.Month);

    protected static SubscriptionPlan BasicPlan() => new(7126958, "basic-plan", "Basic Plan", 29.00m, 1, BillingIntervalUnit.Month);

    protected static CustomerSubscription Subscription(int id = 42,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = "eshop-pro",
        string userReference = UserReference,
        decimal planPrice = 299.00m,
        bool cancelAtEndOfPeriod = false) =>
        new(id, state, userReference, 33)
        {
            PlanHandle = planHandle,
            PlanName = planHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
            PlanPrice = planPrice,
            CancelAtEndOfPeriod = cancelAtEndOfPeriod,
            CurrentPeriodStartedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            NextBillingAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
        };

    protected static BillingCustomer Customer(int id = 33) => new(id, UserReference, UserReference);

    protected static PlanChangePreview Preview(decimal proratedAdjustment = -241.50m,
        PlanChangeTiming timing = PlanChangeTiming.Immediate) =>
        new(42, "eshop-pro", "basic-plan", timing, proratedAdjustment, 29.00m, 0m, 241.50m, 29.00m);

    protected static UsageRecord Usage(long id = 900, int subscriptionId = 42, decimal quantity = 1m) =>
        new(id, subscriptionId, quantity);
}
