using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A subscription in Maxio Advanced Billing.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}
