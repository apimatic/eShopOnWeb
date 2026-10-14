using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb buyer to their customer record in the billing provider.
/// The primary key is the buyer id, so only one request can be creating the provider customer at a time.
/// </summary>
public class BillingCustomer : BillingClaim, IAggregateRoot
{
    public string BuyerId { get; private set; }

    /// <summary>The deterministic reference sent to the provider; the provider keeps it unique.</summary>
    public string CustomerReference { get; private set; }

    public int? ProviderCustomerId { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string buyerId, string customerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        BuyerId = buyerId;
        CustomerReference = customerReference;
        StartClaim(now);
    }

    public void MarkLinked(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        Status = BillingClaimStatus.Completed;
        UpdatedAt = now;
    }
}
