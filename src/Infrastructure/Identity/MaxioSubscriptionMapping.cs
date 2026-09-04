using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Operational link between an eShopOnWeb identity and its Maxio subscription.
/// Maxio remains the billing system of record; this row is only a local correlation record.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
