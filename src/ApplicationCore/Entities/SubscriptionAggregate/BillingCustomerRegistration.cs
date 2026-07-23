using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The details used to create a billing-provider customer for an eShopOnWeb user.
/// </summary>
public sealed record BillingCustomerRegistration
{
    /// <summary>The idempotency key — the eShopOnWeb user's email/username (plan.md §4.4).</summary>
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    /// <summary>
    /// Builds a registration from an eShopOnWeb username. eShopOnWeb Identity usernames are email
    /// addresses, and the provider requires a first and last name, so a deterministic name is derived
    /// from the local part of the address.
    /// </summary>
    public static BillingCustomerRegistration ForUser(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required to register a billing customer.", nameof(userName));
        }

        var reference = userName.Trim();
        var localPart = reference.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;

        return new BillingCustomerRegistration
        {
            Reference = reference,
            Email = reference,
            FirstName = firstName,
            LastName = "eShopOnWeb Customer"
        };
    }
}
