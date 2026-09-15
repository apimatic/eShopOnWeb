using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper, mapped 1:1 onto a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string? UserName)
{
    public (string FirstName, string LastName) DisplayName()
    {
        var source = !string.IsNullOrWhiteSpace(Email) ? Email : UserName;
        if (string.IsNullOrWhiteSpace(source))
        {
            return ("Shopper", "eShopOnWeb");
        }

        var local = source.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        return (Capitalize(local), "Shopper");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
