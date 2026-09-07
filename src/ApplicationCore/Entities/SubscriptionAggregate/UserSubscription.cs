using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity
{
    public string UserId { get; set; } = null!;
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public long PriceInCents { get; set; }
    public DateTimeOffset CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
