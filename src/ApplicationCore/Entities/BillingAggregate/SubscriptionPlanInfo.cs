namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A subscription plan a shopper can enroll in. Maps to a Maxio product within the configured
/// product family. Money is carried in minor units (cents) exactly as the billing system reports
/// it; presentation formatting is left to the caller.
/// </summary>
public sealed record SubscriptionPlanInfo
{
    /// <summary>Stable API handle used to subscribe to this plan.</summary>
    public required string Handle { get; init; }

    public int? ProductId { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numeric length of one billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; init; }
}
