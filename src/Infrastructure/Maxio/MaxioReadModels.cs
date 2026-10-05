using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A subscription plan (a Billing API product) offered to eShopOnWeb shoppers.
/// </summary>
public class MaxioPlan
{
    public int ProductId { get; set; }

    public string? Handle { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public string? ProductFamilyHandle { get; set; }
}

/// <summary>
/// A shopper-facing projection of a Billing API subscription.
/// </summary>
public class MaxioSubscriptionSummary
{
    public int SubscriptionId { get; set; }

    public string? State { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public int? ProductId { get; set; }

    public string? ProductHandle { get; set; }

    public string? ProductName { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>
    /// When the current billing period ends and the next renewal will occur.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public bool IsLive { get; set; }
}