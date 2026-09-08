using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// An eShopOnWeb shopper as seen by the billing system. Maxio is the system of record; the
/// shopper is correlated to a Maxio customer through the stable <see cref="CustomerReference"/>.
/// </summary>
public sealed class SubscriberAccount
{
    public SubscriberAccount(string userId, string email)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        UserId = userId;
        Email = email;
    }

    public string UserId { get; }

    public string Email { get; }

    /// <summary>
    /// The Maxio customer reference for this eShopOnWeb user. It is derived from the stable
    /// ASP.NET identity user id so the mapping survives email changes.
    /// </summary>
    public string CustomerReference => $"eshop-{UserId}";

    public string FirstName => DeriveNames(Email).FirstName;

    public string LastName => DeriveNames(Email).LastName;

    /// <summary>
    /// eShopOnWeb accounts do not capture personal names, so a deterministic billing name is
    /// derived from the account email. Centralised here so the mapping can be replaced with a
    /// real profile lookup by an app that has one.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var address = email ?? string.Empty;
        var at = address.IndexOf('@');
        var local = at >= 0 ? address[..at] : address;
        var domain = at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : string.Empty;

        var first = string.IsNullOrWhiteSpace(local) ? address : local;
        var last = domain;
        var dot = last.IndexOf('.');
        if (dot > 0)
        {
            last = last[..dot];
        }

        if (string.IsNullOrWhiteSpace(last))
        {
            last = "Customer";
        }

        return (Truncate(first.Trim(), 50), Truncate(last.Trim(), 50));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
