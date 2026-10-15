using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// The primary key is the normalized user name, so inserting the row is the claim that
/// stops two concurrent requests from creating two billing customers for one user.
/// </summary>
public class BillingCustomerLink : IAggregateRoot
{
    public string UserKey { get; private set; }
    public string CustomerReference { get; private set; }
    public int? BillingCustomerId { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concurrency token: changes on every update so two writers cannot both take over a claim.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }

    public BillingCustomerLink(string userKey, string customerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        UserKey = userKey;
        CustomerReference = customerReference;
        Status = BillingClaimStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public bool IsLinked => Status == BillingClaimStatus.Completed && BillingCustomerId.HasValue;

    /// <summary>A pending claim whose holder has gone quiet for longer than the lease may be taken over.</summary>
    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan lease) =>
        Status != BillingClaimStatus.Completed && now - UpdatedAt > lease;

    public void Link(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        Status = BillingClaimStatus.Completed;
        Touch(now);
    }

    public void Renew(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
