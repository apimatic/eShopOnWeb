using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.UnitTests.Builders;

public class SubscriptionBuilder
{
    public const string ProPlanHandle = "eshop-pro";
    public const string BasicPlanHandle = "basic-plan";

    public static SubscriberIdentity Subscriber(string userName = "demouser@microsoft.com") =>
        new(userName, userName);

    public static SubscriptionPlan Plan(string handle = ProPlanHandle, long priceInCents = 29900,
        bool requiresPaymentMethod = false) => new()
        {
            Id = 7126957,
            Handle = handle,
            Name = handle == ProPlanHandle ? "Pro Plan" : "Basic Plan",
            PriceInCents = priceInCents,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            ProductFamilyHandle = "eshop-subscribe",
            RequiresPaymentMethod = requiresPaymentMethod
        };

    public static BillingCustomer Customer(long id = 42, string reference = "eshop:demouser@microsoft.com") => new()
    {
        Id = id,
        Reference = reference,
        Email = "demouser@microsoft.com",
        FirstName = "Demouser",
        LastName = "Customer"
    };

    public static CustomerSubscription Subscription(long id = 900, string state = SubscriptionStates.Active,
        string planHandle = ProPlanHandle, string? reference = null, long customerId = 42) => new()
        {
            Id = id,
            State = state,
            Reference = reference,
            CustomerId = customerId,
            PlanHandle = planHandle,
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
}
