using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A claim this application takes in its own store before it asks the billing provider to create
/// something. The claim is keyed by a deterministic reference, so a second request for the same thing
/// (a double-click, a retry, a second instance) is refused by the store's primary key before it can
/// reach the provider.
/// </summary>
public abstract class BillingClaim : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    protected BillingClaim() { }

    protected BillingClaim(string reference, string buyerId, DateTimeOffset now)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        State = BillingClaimState.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The deterministic reference sent to the provider; the primary key of the claim.</summary>
    public string Reference { get; private set; }

    /// <summary>The user name of the shopper that owns the claim.</summary>
    public string BuyerId { get; private set; }

    public BillingClaimState State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// True when the claim has not reached <see cref="BillingClaimState.Completed"/> and has not been touched
    /// for longer than <paramref name="staleAfter"/>, i.e. the request that took it is gone.
    /// </summary>
    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        State != BillingClaimState.Completed && now - UpdatedAt > staleAfter;

    public void MarkUnknown(DateTimeOffset now)
    {
        State = BillingClaimState.Unknown;
        UpdatedAt = now;
    }

    protected void MarkCompleted(DateTimeOffset now)
    {
        State = BillingClaimState.Completed;
        UpdatedAt = now;
    }
}
