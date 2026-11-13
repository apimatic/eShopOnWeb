using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A durable claim on a billing write (create a billing customer, create a subscription).
/// The string <see cref="Key"/> is the primary key, so the store itself refuses a second claim
/// for the same operation: that refusal is what stops a double-click from reaching the billing
/// provider twice. The row also records the outcome once the provider has answered.
/// </summary>
public class BillingClaim : IAggregateRoot
{
    public const int KeyMaxLength = 200;
    public const int ReferenceMaxLength = 100;

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingClaim() { }
    #pragma warning restore CS8618

    public BillingClaim(string key, BillingClaimKind kind, string userName, string providerReference,
        string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(providerReference, nameof(providerReference));

        Key = key;
        Kind = kind;
        UserName = userName;
        ProviderReference = providerReference;
        PlanHandle = planHandle;
        Status = BillingClaimStatus.Pending;
        CreatedUtc = now;
        UpdatedUtc = now;
        Version = Guid.NewGuid();
    }

    /// <summary>Primary key, derived deterministically from what the claim is for.</summary>
    public string Key { get; private set; }

    public BillingClaimKind Kind { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    /// <summary>The eShopOnWeb user name the claim belongs to.</summary>
    public string UserName { get; private set; }

    /// <summary>
    /// The reference this application sends to the provider with the write (customer reference or
    /// subscription reference). It is how an ambiguous outcome is looked up again.
    /// </summary>
    public string ProviderReference { get; private set; }

    public string? PlanHandle { get; private set; }

    public int? ProviderCustomerId { get; private set; }

    public int? ProviderSubscriptionId { get; private set; }

    public DateTimeOffset CreatedUtc { get; private set; }

    public DateTimeOffset UpdatedUtc { get; private set; }

    /// <summary>Optimistic-concurrency token: two callers cannot both take over the same claim.</summary>
    public Guid Version { get; private set; }

    public bool IsStale(DateTimeOffset now, TimeSpan pendingTimeout) =>
        Status == BillingClaimStatus.Unknown ||
        (Status == BillingClaimStatus.Pending && now - UpdatedUtc > pendingTimeout);

    public void MarkCompleted(int? providerCustomerId, int? providerSubscriptionId, DateTimeOffset now)
    {
        Status = BillingClaimStatus.Completed;
        ProviderCustomerId = providerCustomerId ?? ProviderCustomerId;
        ProviderSubscriptionId = providerSubscriptionId ?? ProviderSubscriptionId;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    /// <summary>Re-arms a stale, unknown or finished claim for a new attempt by the caller that won it.</summary>
    public void Restart(string providerReference, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(providerReference, nameof(providerReference));
        Status = BillingClaimStatus.Pending;
        ProviderReference = providerReference;
        ProviderSubscriptionId = null;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedUtc = now;
        Version = Guid.NewGuid();
    }
}

public enum BillingClaimKind
{
    Customer = 1,
    Subscription = 2
}

public enum BillingClaimStatus
{
    /// <summary>Claimed; the provider call is in flight (or its process died — see <see cref="BillingClaim.IsStale"/>).</summary>
    Pending = 1,
    /// <summary>The provider holds the result recorded on the claim.</summary>
    Completed = 2,
    /// <summary>The provider may or may not have acted; the next request for this key settles it by re-reading.</summary>
    Unknown = 3
}
