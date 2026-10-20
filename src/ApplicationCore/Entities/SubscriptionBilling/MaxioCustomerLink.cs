using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// Links an eShop user to its Maxio customer. The primary key is the eShop user name, so the store itself
/// refuses a second claim for the same user: this row is written BEFORE the provider is called.
/// The user name is also the Maxio customer reference.
/// </summary>
public class MaxioCustomerLink
{
#pragma warning disable CS8618 // Required by Entity Framework
    private MaxioCustomerLink() { }
#pragma warning restore CS8618

    public MaxioCustomerLink(string userName, DateTimeOffset claimedAtUtc)
    {
        UserName = userName;
        Status = BillingClaimStatus.Pending;
        ClaimedAtUtc = claimedAtUtc;
        LeaseId = Guid.NewGuid();
    }

    public string UserName { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public int? MaxioCustomerId { get; private set; }
    public DateTimeOffset ClaimedAtUtc { get; private set; }

    /// <summary>Concurrency token: changes whenever the claim changes hands or state.</summary>
    public Guid LeaseId { get; private set; }

    public void Complete(int maxioCustomerId)
    {
        Status = BillingClaimStatus.Completed;
        MaxioCustomerId = maxioCustomerId;
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
