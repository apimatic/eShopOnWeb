using System;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Builds the Maxio customer payload for an eShopOnWeb user. Maxio requires first/last name and
/// email, so those are derived from the account name (which is an email address for the seeded
/// users). The user's account name is stored as the customer <c>reference</c>, giving a stable,
/// unique key for idempotent lookups.
/// </summary>
internal static class CustomerIdentity
{
    private const string Organization = "eShopOnWeb";

    public static MaxioCustomer CreateCustomer(string userName)
    {
        var name = userName.Trim();
        var email = ResolveEmail(name);
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        var domain = at > 0 ? email[(at + 1)..] : string.Empty;

        var nameParts = local.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(nameParts.Length > 0 ? nameParts[0] : "eShop");

        string lastName;
        if (nameParts.Length > 1)
        {
            lastName = string.Join(' ', nameParts.Skip(1).Select(Capitalize));
        }
        else
        {
            var domainFirst = domain.Split('.')[0];
            lastName = string.IsNullOrWhiteSpace(domainFirst) ? "Shopper" : Capitalize(domainFirst);
        }

        return new MaxioCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Organization = Organization,
            Reference = name
        };
    }

    private static string ResolveEmail(string name) =>
        name.Contains('@') ? name : $"{name}@localhost.local";

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
