namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscription plan available for purchase, as read from the billing system of record.
/// </summary>
public record SubscriptionPlanInfo
{
    public string Handle { get; init; } = string.Empty;
    public int? ProductId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool? TrialSupported { get; init; }
    public bool? RequireCreditCard { get; init; }
}
