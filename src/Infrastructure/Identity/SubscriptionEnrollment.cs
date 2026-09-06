using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable application claim used to make subscription enrollment idempotent.</summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string MaxioSubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
