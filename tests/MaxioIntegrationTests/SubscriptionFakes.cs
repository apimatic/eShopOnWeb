using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Domain-level fixtures for the provider-agnostic seam. These tests drive
/// <see cref="SubscriptionService"/> through <see cref="IBillingClient"/>, so they assert the
/// orchestration rules — idempotency, validation, ownership, best-effort eventing — independently of any
/// provider.
/// </summary>
public static class SubscriptionFakes
{
    public const string USER = "demouser@microsoft.com";
    public const string OTHER_USER = "someoneelse@microsoft.com";
    public const int SUBSCRIPTION_ID = 42;
    public const int CUSTOMER_ID = 7;
    public const int COMPONENT_ID = 3062733;

    public static SubscriptionPlan Pro() => new(7130997, MaxioTestContext.PRO_HANDLE, "Pro Plan",
        "Everything in Basic.", 299.00m, 1, BillingIntervalUnit.Month, false, false);

    public static SubscriptionPlan Basic() => new(7130998, MaxioTestContext.BASIC_HANDLE, "Basic Plan",
        "The essentials.", 29.00m, 1, BillingIntervalUnit.Month, false, false);

    public static SubscriptionPlan Archived() => new(999, "retired-plan", "Retired Plan",
        null, 9.00m, 1, BillingIntervalUnit.Month, false, true);

    public static BillingCustomer Customer(string reference = USER) =>
        new(CUSTOMER_ID, reference, reference, "Demo", "User");

    public static MeteredComponentDefinition Component(bool isMetered = true) =>
        new(COMPONENT_ID, MaxioTestContext.COMPONENT_HANDLE, "API Calls", "call", 0.01m, isMetered);

    public static CustomerSubscription Subscription(SubscriptionStatus status = SubscriptionStatus.Active,
        string planHandle = MaxioTestContext.PRO_HANDLE,
        decimal planPrice = 299.00m,
        string customerReference = USER,
        int id = SUBSCRIPTION_ID,
        bool cancelAtEndOfPeriod = false)
    {
        return new CustomerSubscription(id,
            status,
            customerReference,
            CUSTOMER_ID,
            planHandle,
            planHandle == MaxioTestContext.PRO_HANDLE ? "Pro Plan" : "Basic Plan",
            planPrice,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            cancelAtEndOfPeriod,
            null,
            null);
    }

    public static PlanChangePreview Preview(decimal amountDue, string targetHandle = MaxioTestContext.BASIC_HANDLE)
    {
        return new PlanChangePreview(SUBSCRIPTION_ID,
            MaxioTestContext.PRO_HANDLE,
            "Pro Plan",
            299.00m,
            targetHandle,
            "Basic Plan",
            29.00m,
            PlanChangeTiming.Immediately,
            -150.00m,
            14.50m,
            0m,
            amountDue,
            DateTimeOffset.UtcNow);
    }

    public static UsageRecord UsageRecord(decimal quantity = 1m) =>
        new(9001, SUBSCRIPTION_ID, COMPONENT_ID, quantity, "memo", DateTimeOffset.UtcNow);

    /// <summary>A service wired to the given billing client, with recording fakes for the rest.</summary>
    public static SubscriptionService Service(IBillingClient billingClient, IPublisher? publisher = null)
    {
        return new SubscriptionService(
            billingClient,
            publisher ?? Substitute.For<IPublisher>(),
            Substitute.For<IAppLogger<SubscriptionService>>());
    }
}
