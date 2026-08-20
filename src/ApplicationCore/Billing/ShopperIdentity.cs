using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Identity of an eShopOnWeb shopper used to bind a Maxio customer.
/// <see cref="UserId"/> is stored as the Maxio customer <c>reference</c>.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string FirstName, string LastName)
{
    public static ShopperIdentity FromAccount(string userId, string? email, string? userName)
    {
        var resolvedEmail = email ?? userName ?? "shopper@example.com";
        var (firstName, lastName) = SplitName(resolvedEmail, userName);
        return new ShopperIdentity(userId, resolvedEmail, firstName, lastName);
    }

    public static (string FirstName, string LastName) SplitName(string email, string? userName)
    {
        var local = email.Contains('@', StringComparison.Ordinal)
            ? email[..email.IndexOf('@')]
            : (userName ?? email);

        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

        var trimmed = value.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();
    }
}
