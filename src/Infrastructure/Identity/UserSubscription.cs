using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A recurring subscription held by an eShopOnWeb user, backed by Maxio Advanced Billing
/// (the billing system of record). One row per (user, plan) — the unique index makes
/// double-click subscribe attempts idempotent.
/// </summary>
public class UserSubscription
{
    public int Id { get; set; }

    /// <summary>Id of the eShopOnWeb user (ApplicationUser.Id).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Maxio customer id the user is enrolled as (reference = user id).</summary>
    public int MaxioCustomerId { get; set; }

    /// <summary>Maxio subscription id (the billing system of record's id).</summary>
    public int MaxioSubscriptionId { get; set; }

    /// <summary>Maxio product handle of the subscribed plan.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Display name of the plan at subscribe time.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Recurring price in cents at subscribe time.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Last known Maxio subscription state.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Next billing date (Maxio next_assessment_at).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}