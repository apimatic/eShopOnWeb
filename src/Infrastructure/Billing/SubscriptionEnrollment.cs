using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionEnrollment
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
    public const string Indeterminate = "indeterminate";

    private SubscriptionEnrollment()
    {
    }

    public SubscriptionEnrollment(string userId, string productHandle, string subscriptionReference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        SubscriptionReference = subscriptionReference;
        Status = Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; } = Pending;
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Confirm(int maxioSubscriptionId)
    {
        MaxioSubscriptionId = maxioSubscriptionId;
        Status = Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRejected()
    {
        Status = Rejected;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkIndeterminate()
    {
        Status = Indeterminate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Retry()
    {
        MaxioSubscriptionId = null;
        Status = Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
