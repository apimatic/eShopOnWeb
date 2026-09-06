using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class SubscriptionPlan
{
    public long Id { get; set; }
    public string Handle { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long ProductFamilyId { get; set; }
    public long? PriceInCents { get; set; }
    public int? IntervalUnit { get; set; }
    public string? Interval { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
