using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user.
/// <see cref="Reference"/> carries the stable eShopOnWeb user reference and is what makes
/// customer creation idempotent across repeated subscribe calls.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }

    /// <summary>The eShopOnWeb user reference (email / username) this provider customer is keyed on.</summary>
    public string? Reference { get; init; }

    public string? Email { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
