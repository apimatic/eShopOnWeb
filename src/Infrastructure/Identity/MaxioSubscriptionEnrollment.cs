using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local correlation record only. Maxio remains the billing system of record.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
}
