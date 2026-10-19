using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum BillingClaimStatus
{
    InProgress = 0,
    Completed = 1,
    Unknown = 2
}

/// <summary>
/// A primary-key claim taken before a Maxio write. The key is the provider reference,
/// so a second insert is refused by the store before another call is sent.
/// </summary>
public class BillingClaim
{
    private BillingClaim()
    {
        Reference = string.Empty;
    }

    public BillingClaim(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A billing claim reference is required.", nameof(reference));

        Reference = reference;
        Status = BillingClaimStatus.InProgress;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string Reference { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkCompleted()
    {
        Status = BillingClaimStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkUnknown()
    {
        Status = BillingClaimStatus.Unknown;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
