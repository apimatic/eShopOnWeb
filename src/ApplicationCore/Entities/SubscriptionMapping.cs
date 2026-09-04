using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable correlation between an eShop user and the corresponding Maxio records.
/// The Maxio references are also deterministic, so the integration can recover if
/// a request succeeds in Maxio before the local database is written.
/// </summary>
public class SubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
