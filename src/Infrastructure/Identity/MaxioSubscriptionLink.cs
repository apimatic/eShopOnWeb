using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local ownership link for an Advanced Billing subscription. Maxio remains the billing system of record.
/// </summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required int MaxioSubscriptionId { get; set; }
    public required string ProductHandle { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}
