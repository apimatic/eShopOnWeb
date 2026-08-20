using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's subscription as reported by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool Created { get; set; }
}
