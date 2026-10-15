using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// One shopper's subscription to one plan. The composite key (UserKey, PlanHandle) is the claim:
/// the row is inserted before Maxio is called, so a second concurrent request is refused by the store.
/// </summary>
public class SubscriptionClaim
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Unknown = "Unknown";

    public string UserKey { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = Pending;
    public string Reference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
