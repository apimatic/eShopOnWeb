using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A claim on a billing-provider write (creating a customer or a subscription) for one buyer.
/// The primary key is the deterministic reference sent to the provider, so a second request for the
/// same write is refused by the store before it can reach the provider. The row then records the
/// provider id the write produced, which makes it the buyer ↔ provider mapping as well.
/// </summary>
public class BillingClaim : IAggregateRoot
{
    public string Reference { get; private set; }
    public BillingClaimKind Kind { get; private set; }
    public string BuyerId { get; private set; }
    public string? PlanHandle { get; private set; }
    public BillingClaimState State { get; private set; }

    /// <summary>Changes on every ownership transfer; used as the optimistic-concurrency token.</summary>
    public Guid ClaimToken { get; private set; }

    public int? ProviderCustomerId { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingClaim() { }

    public BillingClaim(string reference, BillingClaimKind kind, string buyerId, string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Reference = reference;
        Kind = kind;
        BuyerId = buyerId;
        PlanHandle = planHandle;
        State = BillingClaimState.Claimed;
        ClaimToken = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>True when no request is (or can still be) working on this claim.</summary>
    public bool IsLeaseExpired(DateTimeOffset now, TimeSpan lease) => now - UpdatedAt >= lease;

    public void TakeOver(DateTimeOffset now)
    {
        State = BillingClaimState.Claimed;
        ClaimToken = Guid.NewGuid();
        UpdatedAt = now;
    }

    public void CompleteCustomer(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        State = BillingClaimState.Completed;
        UpdatedAt = now;
    }

    public void CompleteSubscription(int providerCustomerId, int providerSubscriptionId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        ProviderSubscriptionId = providerSubscriptionId;
        State = BillingClaimState.Completed;
        UpdatedAt = now;
    }

    /// <summary>The write may or may not have reached the provider; settle by re-reading before writing again.</summary>
    public void MarkUnknown(DateTimeOffset now)
    {
        State = BillingClaimState.Unknown;
        UpdatedAt = now;
    }

    /// <summary>The provider definitively refused the write; the claim may be taken again straight away.</summary>
    public void Release(DateTimeOffset now)
    {
        State = BillingClaimState.Released;
        UpdatedAt = now;
    }
}
