using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string PlanHandle { get; set; }
    public required string SubscriptionReference { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
