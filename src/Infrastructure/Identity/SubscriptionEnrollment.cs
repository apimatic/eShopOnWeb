using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class SubscriptionEnrollment
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        string ownerToken,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        OwnerToken = ownerToken;
        LeaseExpiresAt = leaseExpiresAt;
        Status = SubscriptionEnrollmentStatus.Creating;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public string Status { get; private set; } = SubscriptionEnrollmentStatus.Creating;
    public string? OwnerToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public string? PlanName { get; private set; }
    public long? PriceInCents { get; private set; }
    public string? BillingInterval { get; private set; }
    public string? ProviderState { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public bool IsOwnedBy(string ownerToken) =>
        string.Equals(OwnerToken, ownerToken, StringComparison.Ordinal);

    public bool TryTakeOwnership(string ownerToken, DateTimeOffset leaseExpiresAt, DateTimeOffset now)
    {
        if (Status == SubscriptionEnrollmentStatus.Active ||
            (Status == SubscriptionEnrollmentStatus.Creating && LeaseExpiresAt > now))
        {
            return false;
        }

        Status = SubscriptionEnrollmentStatus.Creating;
        OwnerToken = ownerToken;
        LeaseExpiresAt = leaseExpiresAt;
        UpdatedAt = now;
        return true;
    }

    public void MarkActive(
        int customerId,
        int subscriptionId,
        string planName,
        long priceInCents,
        string billingInterval,
        string providerState,
        DateTimeOffset? nextBillingDate)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        PlanName = planName;
        PriceInCents = priceInCents;
        BillingInterval = billingInterval;
        ProviderState = providerState;
        NextBillingDate = nextBillingDate;
        Status = SubscriptionEnrollmentStatus.Active;
        OwnerToken = null;
        LeaseExpiresAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = SubscriptionEnrollmentStatus.Failed;
        OwnerToken = null;
        LeaseExpiresAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public static class SubscriptionEnrollmentStatus
{
    public const string Creating = "Creating";
    public const string Active = "Active";
    public const string Failed = "Failed";
}
