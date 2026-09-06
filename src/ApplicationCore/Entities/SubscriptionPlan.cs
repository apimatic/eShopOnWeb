using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SubscriptionPlan : BaseEntity, IAggregateRoot
{
    public int MaxioPlanId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int IntervalUnit { get; set; }
    public string Interval { get; set; } = string.Empty;
}
