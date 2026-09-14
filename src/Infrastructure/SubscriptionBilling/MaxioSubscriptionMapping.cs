using System;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public class MaxioSubscriptionMapping
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public string UniquenessToken { get; set; } = string.Empty;
    public string CreationStatus { get; set; } = SubscriptionCreationStatus.Pending;
    public long MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public string? State { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SubscriptionCreationStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
