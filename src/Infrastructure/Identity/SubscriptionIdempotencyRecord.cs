using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class SubscriptionIdempotencyRecord
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public string Status { get; set; } = SubscriptionIdempotencyStatus.Pending;

    public string? ResponseJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SubscriptionIdempotencyStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
