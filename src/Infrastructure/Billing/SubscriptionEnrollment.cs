using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionEnrollment
{
    public string Reference { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
