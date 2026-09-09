using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Details of a subscription as recorded in the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public long PriceCents { get; set; }
    public int BillingInterval { get; set; }
    public string? BillingIntervalUnit { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CanceledAt { get; set; }
    public long? CustomerId { get; set; }
}
