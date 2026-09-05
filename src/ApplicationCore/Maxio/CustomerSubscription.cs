using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio subscription belonging to the eShopOnWeb user's Maxio customer.
/// </summary>
public class CustomerSubscription
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
}
