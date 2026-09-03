using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The stable identity of an eShopOnWeb shopper as it maps onto a Maxio customer.
/// <see cref="Reference"/> is the durable, unique key used both to look the customer up
/// idempotently and as the Maxio customer <c>reference</c> when one has to be created, so a
/// repeated subscribe for the same shopper always resolves to the same customer.
/// </summary>
public sealed record SubscriberIdentity
{
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }

    /// <summary>
    /// Builds an identity from the authenticated user's name claim (an email-style username in
    /// eShopOnWeb). The username is used verbatim as the stable reference and email; first/last
    /// name are derived from it purely to satisfy Maxio's required-name fields.
    /// </summary>
    public static SubscriberIdentity FromUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A user name is required to identify the subscriber.", nameof(userName));

        var trimmed = userName.Trim();
        var localPart = trimmed.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        return new SubscriberIdentity
        {
            Reference = trimmed,
            Email = trimmed,
            FirstName = firstName,
            LastName = "eShopOnWeb"
        };
    }
}
