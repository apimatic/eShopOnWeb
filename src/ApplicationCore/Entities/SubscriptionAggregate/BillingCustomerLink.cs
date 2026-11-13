using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user to the customer that represents them in the billing system.
/// The row doubles as a claim: it is inserted (keyed by <see cref="CustomerReference"/>) BEFORE the
/// billing system is asked to create the customer, so a second concurrent request for the same user is
/// refused by the store's primary key instead of reaching the billing system.
/// </summary>
public class BillingCustomerLink : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }
    #pragma warning restore CS8618

    public BillingCustomerLink(string customerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        CustomerReference = customerReference;
        Status = BillingCustomerLinkStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The stable reference the billing system knows this user by (primary key).</summary>
    public string CustomerReference { get; private set; }

    public BillingCustomerLinkStatus Status { get; private set; }

    /// <summary>The billing system's id for the customer, once linked.</summary>
    public int? BillingCustomerId { get; private set; }

    /// <summary>Concurrency token: changes every time the claim changes hands or state.</summary>
    public Guid ClaimToken { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsLinked => Status == BillingCustomerLinkStatus.Linked && BillingCustomerId.HasValue;

    public bool IsStale(DateTimeOffset now, TimeSpan lease) =>
        Status == BillingCustomerLinkStatus.Pending && now - ClaimedAt > lease;

    public void Link(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        Status = BillingCustomerLinkStatus.Linked;
        ClaimToken = Guid.NewGuid();
        UpdatedAt = now;
    }

    /// <summary>Takes over a stale pending claim left behind by a request that never finished.</summary>
    public void Reclaim(DateTimeOffset now)
    {
        Status = BillingCustomerLinkStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }
}

public enum BillingCustomerLinkStatus
{
    Pending = 0,
    Linked = 1
}
