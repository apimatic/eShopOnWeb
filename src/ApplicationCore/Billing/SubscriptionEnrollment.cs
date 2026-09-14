using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum SubscriptionEnrollmentState
{
    Pending,
    Active,
    NeedsReconciliation,
    Rejected
}

public enum ReconciliationTarget
{
    None,
    Customer,
    Subscription
}

public sealed class SubscriptionEnrollment
{
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ProductHandle = productHandle;
        CustomerReference = customerReference;
        SubscriptionReference = subscriptionReference;
        State = SubscriptionEnrollmentState.Pending;
        LeaseOwner = leaseOwner;
        LeaseExpiresAt = leaseExpiresAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string CustomerReference { get; private set; } = string.Empty;
    public string SubscriptionReference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public SubscriptionEnrollmentState State { get; private set; }
    public ReconciliationTarget ReconciliationTarget { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public string? ProviderState { get; private set; }
    public string? LastSafeError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public bool HasLiveLease(DateTimeOffset now) =>
        LeaseOwner is not null && LeaseExpiresAt > now;

    public void AcquireLease(string owner, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        LeaseOwner = owner;
        LeaseExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    public void RecordCustomer(int customerId, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        State = SubscriptionEnrollmentState.Pending;
        ReconciliationTarget = ReconciliationTarget.None;
        LastSafeError = null;
        UpdatedAt = now;
    }

    public void Confirm(int customerId, int subscriptionId, string? providerState, DateTimeOffset now)
    {
        MaxioCustomerId = customerId;
        MaxioSubscriptionId = subscriptionId;
        ProviderState = providerState;
        State = SubscriptionEnrollmentState.Active;
        ReconciliationTarget = ReconciliationTarget.None;
        LastSafeError = null;
        ReleaseLease(now);
    }

    public void MarkNeedsReconciliation(ReconciliationTarget target, string safeError, DateTimeOffset now)
    {
        State = SubscriptionEnrollmentState.NeedsReconciliation;
        ReconciliationTarget = target;
        LastSafeError = safeError;
        ReleaseLease(now);
    }

    public void Reject(string safeError, DateTimeOffset now)
    {
        State = SubscriptionEnrollmentState.Rejected;
        LastSafeError = safeError;
        ReleaseLease(now);
    }

    public void ReleaseLease(DateTimeOffset now)
    {
        LeaseOwner = null;
        LeaseExpiresAt = null;
        UpdatedAt = now;
    }
}
