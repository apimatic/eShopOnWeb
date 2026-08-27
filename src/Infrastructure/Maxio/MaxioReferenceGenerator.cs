using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class MaxioReferenceGenerator
{
    public static string Customer(string userId) => "eshop-c-" + Hash(userId);

    public static string Subscription(string userId, string productHandle) =>
        "eshop-s-" + Hash(userId + "\n" + productHandle);

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }
}
