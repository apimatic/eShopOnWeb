using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public enum MaxioSubscriptionLinkStatus
{
    Pending,
    Succeeded,
    OutcomeUnknown,
    Rejected
}

public class MaxioSubscriptionLink
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public MaxioSubscriptionLinkStatus Status { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
