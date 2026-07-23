using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>Terse construction of the domain read models the service orchestrates over.</summary>
internal static class SubscriptionBuilder
{
    public const string UserReference = "customer@microsoft.com";
    public const string ProPlanHandle = "eshop-pro";
    public const string BasicPlanHandle = "basic-plan";
    public const string MeteredComponentHandle = "api-call";

    public static Subscription Subscription(int id = 100,
        SubscriptionState state = SubscriptionState.Active,
        string planHandle = ProPlanHandle,
        long planPriceInCents = 29_900,
        string customerReference = UserReference,
        bool cancelAtEndOfPeriod = false) =>
        new(id,
            customerId: 42,
            customerReference: customerReference,
            planHandle: planHandle,
            planName: planHandle == ProPlanHandle ? "Pro Plan" : "Basic Plan",
            planPriceInCents: planPriceInCents,
            state: state,
            currentPeriodEndsAt: new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            nextAssessmentAt: new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            cancelAtEndOfPeriod: cancelAtEndOfPeriod,
            delayedCancelAt: cancelAtEndOfPeriod ? new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero) : null);

    public static SubscriptionPlan Plan(string handle = ProPlanHandle, long priceInCents = 29_900, int id = 7_130_995) =>
        new(id,
            handle,
            handle == ProPlanHandle ? "Pro Plan" : "Basic Plan",
            "A recurring plan.",
            priceInCents,
            interval: 1,
            intervalUnit: "month",
            requiresPaymentMethod: false,
            productFamilyHandle: "eshop-subscribe");

    public static MeteredComponentDefinition MeteredComponent(bool isMetered = true, decimal? unitPrice = 0.01m) =>
        new(3_062_732,
            MeteredComponentHandle,
            "API Calls",
            isMetered ? "metered_component" : "quantity_based_component",
            isMetered,
            "api call",
            unitPrice,
            "per_unit",
            3_026_729,
            "eshop-subscribe");

    public static BillingCustomer Customer(string reference = UserReference) =>
        new(42, reference, reference, "customer", "eShopOnWeb");

    public static UsageRecord UsageRecord(decimal quantity = 1m, int subscriptionId = 100) =>
        new(9_001, subscriptionId, 3_062_732, MeteredComponentHandle, quantity, "memo", DateTimeOffset.UtcNow);

    public static PlanChangePreview Preview(int subscriptionId = 100,
        string currentPlanHandle = BasicPlanHandle,
        string targetPlanHandle = ProPlanHandle,
        PlanChangeTiming timing = PlanChangeTiming.Immediate,
        long paymentDueInCents = 23_900) =>
        new(subscriptionId,
            currentPlanHandle,
            targetPlanHandle,
            timing,
            proratedAdjustmentInCents: 23_900,
            chargeInCents: 24_900,
            creditAppliedInCents: 1_000,
            paymentDueInCents: paymentDueInCents,
            newPlanPriceInCents: 29_900,
            effectiveAt: null);
}
