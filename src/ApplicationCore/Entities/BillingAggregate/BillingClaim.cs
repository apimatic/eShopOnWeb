using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A duplicate-prevention claim for a billing write (customer creation or subscription creation).
/// The claim key is the deterministic reference sent to the billing provider and is the primary key,
/// so the store itself refuses a second claim for the same write before the provider is called.
/// </summary>
public class BillingClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingClaim() { }
    #pragma warning restore CS8618

    public BillingClaim(string claimKey, BillingClaimKind kind, string userName, string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(claimKey, nameof(claimKey));
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        ClaimKey = claimKey;
        Kind = kind;
        UserName = userName;
        PlanHandle = planHandle;
        Status = BillingClaimStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public string ClaimKey { get; private set; }
    public BillingClaimKind Kind { get; private set; }
    public BillingClaimStatus Status { get; private set; }
    public string UserName { get; private set; }
    public string? PlanHandle { get; private set; }
    public int? ProviderCustomerId { get; private set; }
    public int? ProviderSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token; changes on every state transition.</summary>
    public Guid Version { get; private set; }

    public void MarkCreated(int? providerCustomerId, int? providerSubscriptionId, DateTimeOffset now)
    {
        Status = BillingClaimStatus.Created;
        ProviderCustomerId = providerCustomerId ?? ProviderCustomerId;
        ProviderSubscriptionId = providerSubscriptionId ?? ProviderSubscriptionId;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    /// <summary>Re-takes an <see cref="BillingClaimStatus.Unknown"/> or stale claim once the provider confirmed the write did not land.</summary>
    public void MarkPending(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        Touch(now);
    }

    /// <summary>
    /// A claim still <see cref="BillingClaimStatus.Pending"/> after <paramref name="staleAfter"/> belongs to a request that
    /// died mid-flight; it must be reconciled with the provider rather than refuse callers forever.
    /// </summary>
    public bool IsStalePending(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == BillingClaimStatus.Pending && now - UpdatedAt > staleAfter;

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
