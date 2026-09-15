using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local correlation record only. Subscription state and billing data are always read from Maxio.
/// </summary>
public class MaxioSubscriptionRecord
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
