using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// One subscription per eShop user per plan. The composite primary key (user name, product handle) makes the
/// store refuse a second subscribe for the same plan: this row is written BEFORE the provider is called.
/// <see cref="Reference"/> is derived deterministically from the key and sent to Maxio as the subscription
/// reference, so the outcome of an interrupted create can be found again.
/// </summary>
public class MaxioSubscriptionClaim
{
#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioSubscriptionClaim() { }
#pragma warning restore CS8618

    public MaxioSubscriptionClaim(string userName, string productHandle, string reference, DateTimeOffset claimedAtUtc)
    {
        UserName = userName;
        ProductHandle = productHandle;
        Reference = reference;
        Status = BillingClaimStatus.Pending;
        ClaimedAtUtc = claimedAtUtc;
        LeaseId = Guid.NewGuid();
    }

    public string UserName { get; private set; }
    public string ProductHandle { get; private set; }
    public string Reference { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }
    public DateTimeOffset ClaimedAtUtc { get; private set; }

    /// <summary>Concurrency token: changes whenever the claim changes hands or state.</summary>
    public Guid LeaseId { get; private set; }

    public void Complete(int maxioSubscriptionId)
    {
        Status = BillingClaimStatus.Completed;
        MaxioSubscriptionId = maxioSubscriptionId;
        LeaseId = Guid.NewGuid();
    }

    public void MarkUnknown()
    {
        Status = BillingClaimStatus.Unknown;
        LeaseId = Guid.NewGuid();
    }

    public void TakeOver(DateTimeOffset nowUtc)
    {
        Status = BillingClaimStatus.Pending;
        ClaimedAtUtc = nowUtc;
        LeaseId = Guid.NewGuid();
    }
}
