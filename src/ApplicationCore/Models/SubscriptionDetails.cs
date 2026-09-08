using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A user's subscription in the billing system of record.
/// </summary>
public class SubscriptionDetails
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public bool AlreadyExisted { get; set; }
}
