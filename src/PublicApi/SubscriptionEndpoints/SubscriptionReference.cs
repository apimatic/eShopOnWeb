namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionReference
{
    public static string Customer(string userId) => $"eshop-user:{userId}";

    public static string Subscription(string userId, string productHandle) =>
        $"eshop-subscription:{userId}:{productHandle}";
}
