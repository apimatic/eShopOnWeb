using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SubscriptionEnrollment : BaseEntity
{
    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(2);

    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string userId, string productHandle, string reference)
    {
        UserId = userId;
        ProductHandle = productHandle;
        Reference = reference;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
        ClaimToken = Guid.NewGuid().ToString("N");
        ClaimExpiresAt = CreatedAt.Add(ClaimDuration);
    }

    public string UserId { get; private set; } = string.Empty;
    public string ProductHandle { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string ClaimToken { get; private set; } = string.Empty;
    public DateTimeOffset ClaimExpiresAt { get; private set; }

    public bool HasActiveClaim(DateTimeOffset now) => ClaimExpiresAt > now;

    public void RenewClaim(DateTimeOffset now)
    {
        ClaimToken = Guid.NewGuid().ToString("N");
        ClaimExpiresAt = now.Add(ClaimDuration);
        UpdatedAt = now;
    }

    public void ReleaseClaim()
    {
        ClaimExpiresAt = DateTimeOffset.UtcNow;
        UpdatedAt = ClaimExpiresAt;
    }

    public void ConfirmCustomer(int customerId)
    {
        MaxioCustomerId = customerId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ConfirmSubscription(int subscriptionId)
    {
        MaxioSubscriptionId = subscriptionId;
        UpdatedAt = DateTimeOffset.UtcNow;
        ClaimExpiresAt = UpdatedAt;
    }
}
