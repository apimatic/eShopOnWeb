using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// The reconciled state of a user's subscription, projected from Maxio Advanced Billing.
/// </summary>
public class SubscriptionSummary
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string MaxioReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
