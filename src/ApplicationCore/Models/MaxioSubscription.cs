using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class MaxioSubscription
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string State { get; set; } = null!;
    public long? CurrentPriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
