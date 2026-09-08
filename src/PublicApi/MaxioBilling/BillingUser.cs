using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public sealed record BillingUser(string UserId, string Email, string FirstName, string LastName);

public static class BillingUserFactory
{
    public static BillingUser FromIdentity(string username, string? email)
    {
        var effectiveEmail = !string.IsNullOrWhiteSpace(email)
            ? email!.Trim()
            : username.Contains('@') ? username.Trim()
            : $"{username.Trim()}@users.eshoponweb.local";

        var localPart = effectiveEmail.Split('@')[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var firstName = SanitizeName(nameParts.Length > 0 ? nameParts[0] : null) ?? "eShop";
        var lastName = SanitizeName(nameParts.Length > 1 ? nameParts[1] : null) ?? "Customer";

        return new BillingUser(username.Trim(), effectiveEmail, firstName, lastName);
    }

    private static string? SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var name = value.Length > 50 ? value[..50] : value;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
