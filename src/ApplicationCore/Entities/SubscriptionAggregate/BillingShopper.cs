using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// eShopOnWeb identity mapped onto a Maxio customer.
/// </summary>
public sealed class BillingShopper
{
    public string UserId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>
    /// Stable Maxio customer <c>reference</c> for this shopper. Unique per site.
    /// </summary>
    public string CustomerReference => $"eshop:{UserId}";

    public BillingShopper(string userId, string email, string firstName, string lastName)
    {
        UserId = userId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public static BillingShopper FromIdentity(string userId, string? email, string? userName)
    {
        var address = !string.IsNullOrWhiteSpace(email)
            ? email
            : !string.IsNullOrWhiteSpace(userName)
                ? userName
                : $"{userId}@users.eshop.local";

        var local = address.Contains('@', StringComparison.Ordinal)
            ? address.Split('@')[0]
            : address;
        var tokens = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = tokens.Length > 0 ? Capitalize(tokens[0]) : "Shopper";
        var last = tokens.Length > 1 ? Capitalize(tokens[^1]) : "eShopOnWeb";

        return new BillingShopper(userId, address, first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
