using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Arranges the domain types the provider-agnostic seam hands back.
/// </summary>
public class SubscriptionBuilder
{
    public const string TEST_USER_REFERENCE = "demouser@microsoft.com";
    public const int TEST_CUSTOMER_ID = 55501;
    public const int TEST_SUBSCRIPTION_ID = 15236915;

    public static CustomerSubscription Subscription(SubscriptionState state = SubscriptionState.Active,
        string planHandle = "eshop-pro", decimal planPrice = 299.00m, int id = TEST_SUBSCRIPTION_ID)
    {
        return new CustomerSubscription(id, state, TEST_CUSTOMER_ID, TEST_USER_REFERENCE, planHandle,
            planHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan", planPrice,
            new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.FromHours(-5)), false, null, null);
    }

    public static BillingCustomer Customer() =>
        new BillingCustomer(TEST_CUSTOMER_ID, TEST_USER_REFERENCE, TEST_USER_REFERENCE);

    public static SubscriptionPlan Plan(string handle = "eshop-pro", decimal price = 299.00m) =>
        new SubscriptionPlan(7126957, handle, handle == "eshop-pro" ? "Pro Plan" : "Basic Plan", price, 1, "month", false);

    public static MeteredComponent Component(string kind = MeteredComponent.METERED_KIND) =>
        new MeteredComponent(3057195, "api-call", "API Calls", kind, "per_unit", 0.01m, 3023074);

    public static UsageRecord UsageRecord(decimal quantity = 1m) =>
        new UsageRecord(138522957, TEST_SUBSCRIPTION_ID, 3057195, "api-call", quantity, "memo", DateTimeOffset.UtcNow);

    public static PlanChangePreview Preview(decimal paymentDue, string targetPlanHandle = "basic-plan") =>
        new PlanChangePreview(targetPlanHandle, 270.00m, 29.00m, paymentDue, 270.00m);
}
