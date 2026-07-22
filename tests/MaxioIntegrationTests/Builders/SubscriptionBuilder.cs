using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

public class SubscriptionBuilder
{
    public const string BuyerId = "demo@microsoft.com";

    public static SubscriptionPlan ProPlan { get; } =
        new(7130993, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false);

    public static SubscriptionPlan BasicPlan { get; } =
        new(7130994, "basic-plan", "Basic Plan", null, 2900, 1, "month", false);

    public static Subscription WithState(SubscriptionState state, int id = 101,
        SubscriptionPlan? plan = null, string buyerId = BuyerId) =>
        new(id, 55, buyerId, plan ?? ProPlan, state,
            new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero), false, null, null);

    public static MeteredComponent MeteredApiCall { get; } =
        new(3062731, "api-call", "API Calls", true, "per_unit", 0.01m);

    public static MeteredComponent QuantityBasedApiCall { get; } =
        new(3062731, "api-call", "API Calls", false, "per_unit", 0.01m);

    public static UsageRecord Usage(decimal quantity, int subscriptionId = 101) =>
        new(900, subscriptionId, 3062731, "api-call", quantity, null, DateTimeOffset.UtcNow);
}
