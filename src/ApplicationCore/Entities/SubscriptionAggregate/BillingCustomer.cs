using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShop user to their customer record in the billing system.
/// The primary key is the customer reference sent to the provider, so only one row can ever exist per user.
/// </summary>
public class BillingCustomer : IBillingClaim, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string customerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        CustomerReference = customerReference;
        Status = BillingClaimStatus.Pending;
        ClaimedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public string CustomerReference { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public int? ProviderId { get; private set; }
    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        ClaimedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void Complete(int providerId, DateTimeOffset now)
    {
        Status = BillingClaimStatus.Completed;
        ProviderId = providerId;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
