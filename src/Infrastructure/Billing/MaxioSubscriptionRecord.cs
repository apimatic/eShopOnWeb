using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SiteSubdomain { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public long MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
