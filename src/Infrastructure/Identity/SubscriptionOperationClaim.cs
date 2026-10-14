using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable claim that prevents duplicate Maxio writes for an application identity.</summary>
public class SubscriptionOperationClaim
{
    public string Key { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
