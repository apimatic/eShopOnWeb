using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// The primary key is the user name, so inserting a second link for the same user is refused by the store;
/// that refusal is what keeps two concurrent requests from both creating a provider customer.
/// </summary>
public class BillingCustomerLink
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }

    public BillingCustomerLink(string userName, string customerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        UserName = userName;
        CustomerReference = customerReference;
        Status = BillingClaimStatus.Pending;
        Version = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string UserName { get; private set; }

    /// <summary>The deterministic reference sent to the provider; the provider refuses a second customer with it.</summary>
    public string CustomerReference { get; private set; }

    public int? ProviderCustomerId { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    /// <summary>Optimistic-concurrency token: a takeover of a stale claim succeeds for one caller only.</summary>
    public Guid Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Complete(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        Status = BillingClaimStatus.Completed;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        Version = Guid.NewGuid();
        UpdatedAt = now;
    }
}
