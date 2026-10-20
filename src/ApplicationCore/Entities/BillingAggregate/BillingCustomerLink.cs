using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// The primary key is the customer reference derived from the user, so inserting the row is the
/// claim that stops a second caller from creating a second billing customer for the same user.
/// </summary>
public class BillingCustomerLink : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }
    #pragma warning restore CS8618

    public BillingCustomerLink(string customerReference, DateTimeOffset claimedAt)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));

        CustomerReference = customerReference;
        Status = BillingClaimStatus.Pending;
        ClaimedAt = claimedAt;
        UpdatedAt = claimedAt;
        Version = Guid.NewGuid();
    }

    /// <summary>Primary key: the reference sent to the billing provider for this user's customer.</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The provider's customer id, once known.</summary>
    public int? ProviderCustomerId { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concurrency token: changes on every mutation so two callers cannot both take over a stale claim.</summary>
    public Guid Version { get; private set; }

    public void Complete(int providerCustomerId, DateTimeOffset at)
    {
        Guard.Against.NegativeOrZero(providerCustomerId, nameof(providerCustomerId));

        ProviderCustomerId = providerCustomerId;
        Status = BillingClaimStatus.Completed;
        UpdatedAt = at;
        Version = Guid.NewGuid();
    }
}
