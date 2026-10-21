using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// Keyed by the user id, so inserting the row is the claim that stops two concurrent
/// requests from both creating a provider customer for the same user.
/// </summary>
public class BillingCustomerLink : IAggregateRoot
{
    public string UserId { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }

    public static BillingCustomerLink Claim(string userId, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        return new BillingCustomerLink
        {
            UserId = userId,
            Status = BillingClaimStatus.Pending,
            ClaimedAt = now,
            ConcurrencyStamp = Guid.NewGuid()
        };
    }

    public bool IsCompleted => Status == BillingClaimStatus.Completed && BillingCustomerId.HasValue;

    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan lease) =>
        Status == BillingClaimStatus.Pending && now - ClaimedAt > lease;

    public void Complete(int billingCustomerId, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        BillingCustomerId = billingCustomerId;
        Status = BillingClaimStatus.Completed;
        CompletedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>Takes over a stale pending claim whose original request never settled.</summary>
    public void Renew(DateTimeOffset now)
    {
        ClaimedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
