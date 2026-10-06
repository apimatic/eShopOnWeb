using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// A recurring subscription as recorded by Maxio Advanced Billing (the billing system of record).
/// </summary>
public record BillingSubscription
{
    public long Id { get; init; }
    public string Reference { get; init; } = string.Empty;
    /// <summary>Maxio subscription state: active, trialing, past_due, canceled, expired, ...</summary>
    public string State { get; init; } = string.Empty;
    public long CustomerId { get; init; }
    public string? CustomerReference { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    /// <summary>When the next payment will be assessed (spec: next_assessment_at).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
