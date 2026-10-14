using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb identity user to their customer record in the billing system.
/// The row is inserted (claimed) before the billing customer is created, and its primary key
/// (<see cref="UserId"/>) is what stops two concurrent requests from creating two customers.
/// </summary>
public class BillingCustomer : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string userId, string reference, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        UserId = userId;
        Reference = reference;
        ClaimedAt = claimedAt;
    }

    public string UserId { get; private set; }

    /// <summary>Our own reference for the customer in the billing system; used to look it up again.</summary>
    public string Reference { get; private set; }

    public int? BillingCustomerId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset? ProvisionedAt { get; private set; }

    public bool IsProvisioned => BillingCustomerId.HasValue;

    public void MarkProvisioned(int billingCustomerId, DateTimeOffset provisionedAt)
    {
        Guard.Against.NegativeOrZero(billingCustomerId, nameof(billingCustomerId));
        BillingCustomerId = billingCustomerId;
        ProvisionedAt = provisionedAt;
    }
}
