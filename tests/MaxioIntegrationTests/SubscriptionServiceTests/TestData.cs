using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>Domain fixtures shared by the seam tests.</summary>
public static class TestData
{
    public const long CustomerId = 97882982;
    public const long SubscriptionId = 93491148;
    public const string BuyerId = "demouser@microsoft.com";

    public static readonly DateTimeOffset PeriodStart = new(2026, 7, 23, 20, 12, 8, TimeSpan.FromHours(5));
    public static readonly DateTimeOffset PeriodEnd = new(2026, 8, 23, 20, 12, 8, TimeSpan.FromHours(5));
    public static readonly DateTimeOffset NextAssessment = PeriodEnd;

    public static BillingPlan ProPlan { get; } =
        new(7130999, "eshop-pro", "Pro Plan", 29900, 1, "month", "eshop-subscribe", false);

    public static BillingPlan BasicPlan { get; } =
        new(7131000, "basic-plan", "Basic Plan", 2900, 1, "month", "eshop-subscribe", false);

    public static BillingCustomer Customer { get; } =
        new(CustomerId, BuyerId, BuyerId, "Demo", "User");

    public static BillingComponent MeteredComponent { get; } =
        new(3062734, "api-call", "API Calls", "metered_component", "api call", 0.01m, "per_unit");

    public static BillingSubscription Subscription(SubscriptionState state = SubscriptionState.Active,
        string productHandle = "eshop-pro",
        string productName = "Pro Plan",
        int productPriceInCents = 29900,
        bool cancelAtEndOfPeriod = false,
        DateTimeOffset? delayedCancelAt = null,
        string? nextProductHandle = null,
        long? id = null) =>
        new(id ?? SubscriptionId,
            state,
            CustomerId,
            BuyerId,
            productHandle,
            productName,
            productPriceInCents,
            PeriodStart,
            PeriodEnd,
            NextAssessment,
            cancelAtEndOfPeriod,
            delayedCancelAt,
            nextProductHandle,
            balanceInCents: 29900,
            currency: "USD");

    public static PlanChangePreview Preview(string targetHandle = "basic-plan",
        PlanChangeTiming timing = PlanChangeTiming.Immediate,
        int proratedAdjustmentInCents = -29900,
        int chargeInCents = 2905,
        int paymentDueInCents = 0,
        int creditAppliedInCents = -26995) =>
        new(targetHandle, timing, proratedAdjustmentInCents, chargeInCents, paymentDueInCents, creditAppliedInCents);
}
