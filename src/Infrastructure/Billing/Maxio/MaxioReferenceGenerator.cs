using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public static class MaxioReferenceGenerator
{
    public static string CustomerReference(string userId) =>
        $"eshop-c-{Hash(userId)[..40]}";

    public static string SubscriptionReference(string userId, string productHandle) =>
        $"eshop-s-{Hash($"{userId}\n{productHandle}")[..40]}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
