using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A view of a subscription held by a shopper, projected from Maxio (Advanced Billing).
/// </summary>
public class SubscriptionDetailsDto
{
    public int SubscriptionId { get; init; }

    public string State { get; init; } = string.Empty;

    public string ProductHandle { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string? ProductDescription { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>The recurring amount actually billed on this subscription, in cents.</summary>
    public long PriceInCents { get; init; }

    public decimal Price { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = "month";

    public int CustomerId { get; init; }

    public string? CustomerReference { get; init; }

    public string? CustomerEmail { get; init; }

    /// <summary>End of the current billing period; the next regularly scheduled billing date.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next payment capture is attempted.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? PaymentCollectionMethod { get; init; }

    public long BalanceInCents { get; init; }

    public string? SubscriptionReference { get; init; }

    public string? Currency { get; init; }
}
