using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb user as presented to Maxio (customer of record).
/// </summary>
public sealed class ShopperIdentity
{
    public ShopperIdentity(string userId, string email, string? userName)
    {
        UserId = userId;
        Email = email;
        var (first, last) = SplitName(email, userName);
        FirstName = first;
        LastName = last;
    }

    public string UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>
    /// Maxio customer <c>reference</c> — unique per site. Uses the eShopOnWeb user id
    /// so a double-create is rejected by Maxio and recoverable via lookup.
    /// </summary>
    public string CustomerReference => $"eshop:{UserId}";

    public string SubscriptionReference(string productHandle) => $"eshop:{UserId}:{productHandle}";

    private static (string First, string Last) SplitName(string email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "Customer");
        }

        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(local), "Customer");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Shopper";
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
