using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Local coordination and recovery record for a Maxio subscription. Maxio remains
/// the system of record for all billing state.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; private set; }
    public string UserId { get; private set; }
    public string ProductHandle { get; private set; }
    public string CustomerReference { get; private set; }
    public string SubscriptionReference { get; private set; }
    public long? MaxioCustomerId { get; private set; }
    public long? MaxioSubscriptionId { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private SubscriptionEnrollment()
    {
        UserId = string.Empty;
        ProductHandle = string.Empty;
        CustomerReference = string.Empty;
        SubscriptionReference = string.Empty;
        Status = string.Empty;
    }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
    {
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        Status = SubscriptionEnrollmentStatus.Creating;
        CreatedAt = now;
        UpdatedAt = now;
        LeaseExpiresAt = leaseExpiresAt;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void RenewLease(DateTimeOffset now, DateTimeOffset leaseExpiresAt)
    {
        Status = SubscriptionEnrollmentStatus.Creating;
        UpdatedAt = now;
        LeaseExpiresAt = leaseExpiresAt;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Complete(long customerId, long subscriptionId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        Status = SubscriptionEnrollmentStatus.Synchronized;
        UpdatedAt = now;
        LeaseExpiresAt = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}

public static class SubscriptionEnrollmentStatus
{
    public const string Creating = "Creating";
    public const string Synchronized = "Synchronized";
}
