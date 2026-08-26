using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as confirmed by the billing system of record.
/// </summary>
public class CustomerSubscriptionDto
{
    public int? Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
