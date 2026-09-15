namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionReference
{
    public static string ForCustomer(string userId) => $"eshop-user-{userId}";

    public static string ForSubscription(string userId, string planHandle) =>
        $"eshop-subscription-{userId}-{planHandle}";
}
