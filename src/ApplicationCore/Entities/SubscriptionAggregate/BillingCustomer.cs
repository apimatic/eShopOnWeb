using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing-provider customer record linked to an eShopOnWeb identity. <see cref="Reference"/> is
/// the stable eShopOnWeb user reference (the signed-in user's email/username) and is what makes
/// customer creation idempotent across repeated subscribe calls.
/// </summary>
public sealed record BillingCustomer
{
    public BillingCustomer(int id, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A customer reference is required.", nameof(reference));

        Id = id;
        Reference = reference;
    }

    public int Id { get; init; }

    public string Reference { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Email { get; init; }
}
