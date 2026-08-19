using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class SubscriptionReference
{
    public static string ForPlan(string userId, string productHandle)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("Product handle is required.", nameof(productHandle));
        }

        return $"{userId}:{productHandle}";
    }

    public static string ForReenrollment(string userId, string productHandle) =>
        $"{ForPlan(userId, productHandle)}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
}
