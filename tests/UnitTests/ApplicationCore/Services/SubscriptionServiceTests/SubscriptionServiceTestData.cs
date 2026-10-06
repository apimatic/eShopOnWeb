using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

internal static class SubscriptionServiceTestData
{
    public const string USER_ID = "1f2a3b4c-5d6e-7f80-91a2-b3c4d5e6f7a8";
    public const string EMAIL = "demouser@microsoft.com";
    public const string FAMILY_HANDLE = "eshop-subscribe";
    public const string PLAN_HANDLE = "eshop-pro";
    public const long PLAN_ID = 7126957;
    public const long CUSTOMER_ID = 3023075;
    public const long SUBSCRIPTION_ID = 7126999;

    public static string CustomerReference => $"eshop-user-{USER_ID}";
    public static string SubscriptionReference => $"eshop-sub-{USER_ID}-{PLAN_HANDLE}";

    public static SubscriberIdentity Subscriber() => new(USER_ID, EMAIL);

    public static SubscriptionPlan ProPlan() => new()
    {
        Id = PLAN_ID,
        Handle = PLAN_HANDLE,
        Name = "eShop Webinars Pro",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
    };

    public static MaxioCustomer Customer() => new()
    {
        Id = CUSTOMER_ID,
        Reference = CustomerReference,
        FirstName = "demouser",
        LastName = "Shopper",
        Email = EMAIL,
    };

    public static MaxioSubscription Subscription(string state = "active", long productId = PLAN_ID, long? id = null) => new()
    {
        Id = id ?? SUBSCRIPTION_ID,
        State = state,
        Reference = SubscriptionReference,
        ProductId = productId,
        ProductHandle = PLAN_HANDLE,
        ProductName = "eShop Webinars Pro",
        PriceInCents = 29900,
        Currency = "USD",
        NextBillingAt = new DateTimeOffset(2030, 1, 5, 0, 0, 0, TimeSpan.Zero),
        ActivatedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
        CreatedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    public static List<SubscriptionPlan> Plans() => new() { ProPlan() };

    public static List<MaxioSubscription> NoSubscriptions() => new();
}
