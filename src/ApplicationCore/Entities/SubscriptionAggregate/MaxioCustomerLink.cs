using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class MaxioCustomerLink : BaseEntity
{
    private MaxioCustomerLink() { }

    public MaxioCustomerLink(string userId, string reference, string ownerToken, DateTimeOffset now, TimeSpan lease)
    {
        UserId = userId;
        Reference = reference;
        Acquire(ownerToken, now, lease);
        CreatedAt = now;
    }

    public string UserId { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public string OperationState { get; private set; } = "Pending";
    public string OwnerToken { get; private set; } = string.Empty;
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string ConcurrencyStamp { get; private set; } = Guid.NewGuid().ToString("N");

    public bool TryAcquire(string ownerToken, DateTimeOffset now, TimeSpan lease)
    {
        if (LeaseExpiresAt > now && !string.Equals(OwnerToken, ownerToken, StringComparison.Ordinal)) return false;
        Acquire(ownerToken, now, lease);
        return true;
    }

    public bool IsOwnedBy(string ownerToken) => string.Equals(OwnerToken, ownerToken, StringComparison.Ordinal);

    public void Complete(int maxioCustomerId, DateTimeOffset now)
    {
        MaxioCustomerId = maxioCustomerId;
        OperationState = "Completed";
        LeaseExpiresAt = now;
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    public void MarkUncertain(DateTimeOffset now, TimeSpan lease)
    {
        OperationState = "Uncertain";
        LeaseExpiresAt = now.Add(lease);
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    private void Acquire(string ownerToken, DateTimeOffset now, TimeSpan lease)
    {
        OwnerToken = ownerToken;
        OperationState = "Pending";
        LeaseExpiresAt = now.Add(lease);
        UpdatedAt = now;
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }
}
