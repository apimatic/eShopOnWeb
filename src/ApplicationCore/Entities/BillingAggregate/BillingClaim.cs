using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A durable claim on one billing-provider write, keyed by the deterministic reference that write carries.
/// The key is the primary key, so the store itself refuses a second claim for the same write: that refusal
/// (not a read-then-insert check) is what stops a double-click from reaching the provider twice.
/// Once the write is settled the claim doubles as the local mapping user to provider record.
/// </summary>
public class BillingClaim : IAggregateRoot
{
    public const int KeyMaxLength = 64;

#pragma warning disable CS8618 // Required by Entity Framework
    private BillingClaim() { }
#pragma warning restore CS8618

    public BillingClaim(string key, BillingClaimKind kind, string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));
        Guard.Against.OutOfRange(key.Length, nameof(key), 1, KeyMaxLength);
        Key = key;
        Kind = kind;
        State = BillingClaimState.Pending;
        PlanHandle = planHandle;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>The provider-side reference of the write this claim guards (primary key).</summary>
    public string Key { get; private set; }

    public BillingClaimKind Kind { get; private set; }

    public BillingClaimState State { get; private set; }

    /// <summary>The provider's id for the record once the write is settled (text: provider ids can outgrow 32 bits).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>For a subscription claim, the plan the caller asked for.</summary>
    public string? PlanHandle { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token: every state change rotates it, so two callers cannot both take over one claim.</summary>
    public Guid Version { get; private set; }

    /// <summary>A pending claim older than <paramref name="lease"/> was abandoned (its caller died mid-call) and may be taken over.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan lease) =>
        State == BillingClaimState.Pending && now - UpdatedAt > lease;

    /// <summary>Takes the claim over (from Unknown or a stale Pending) so this caller may settle it.</summary>
    public void Begin(string? planHandle, DateTimeOffset now)
    {
        State = BillingClaimState.Pending;
        PlanHandle = planHandle ?? PlanHandle;
        Touch(now);
    }

    public void Complete(string externalId, string? planHandle, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(externalId, nameof(externalId));
        State = BillingClaimState.Done;
        ExternalId = externalId;
        PlanHandle = planHandle ?? PlanHandle;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        State = BillingClaimState.Unknown;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}

public enum BillingClaimKind
{
    Customer = 1,
    Subscription = 2
}

public enum BillingClaimState
{
    /// <summary>A caller holds the claim and is talking to the provider right now.</summary>
    Pending = 1,

    /// <summary>The provider confirmed the write; <see cref="BillingClaim.ExternalId"/> holds its id.</summary>
    Done = 2,

    /// <summary>The write may or may not have landed (connection lost after sending); the next caller re-reads the provider by reference.</summary>
    Unknown = 3
}
