namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class SubscriptionReference
{
    public static string For(string userId, string planHandle) => userId + ":" + planHandle;
}
