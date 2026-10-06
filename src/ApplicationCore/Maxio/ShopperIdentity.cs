using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Represents the authenticated eShop shopper for whom a Maxio customer/subscription is ensured.
/// The identity is derived from the caller's JWT (email is the stable reference).
/// </summary>
public class ShopperIdentity
{
    public ShopperIdentity(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Shopper email is required to identify the Maxio customer.", nameof(email));

        Email = email.Trim();
        var local = Email.Contains('@') ? Email.Split('@')[0] : Email;
        FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
        LastName = "eShop";
    }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Stable idempotency key linking the eShop shopper to a Maxio customer.
    /// </summary>
    public string CustomerReference => Email;
}
