using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;

/// <summary>
/// A claim on a deterministic reference, written to this application's own store BEFORE the matching
/// write is sent to the billing provider. The reference is the primary key, so the store itself refuses
/// a second claim for the same reference.
/// </summary>
public abstract class BillingClaim
{
    public const int ReferenceMaxLength = 64;

#pragma warning disable CS8618 // Required by Entity Framework
    protected BillingClaim() { }
#pragma warning restore CS8618

    protected BillingClaim(string reference, DateTimeOffset now)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Guard.Against.OutOfRange(reference.Length, nameof(reference), 1, ReferenceMaxLength);
        Status = BillingClaimStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        AttemptStartedAt = now;
        Version = 1;
    }

    /// <summary>Deterministic reference; the primary key.</summary>
    public string Reference { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    /// <summary>The provider's numeric id once the write is confirmed.</summary>
    public int? ProviderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the most recent provider write attempt for this claim started.</summary>
    public DateTimeOffset AttemptStartedAt { get; private set; }

    /// <summary>Optimistic concurrency token: a take-over of a stale claim must win this race to proceed.</summary>
    public int Version { get; private set; }

    public bool IsStale(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == BillingClaimStatus.Pending && now - UpdatedAt >= staleAfter;

    public void MarkCreated(int providerId, DateTimeOffset now)
    {
        ProviderId = providerId;
        Status = BillingClaimStatus.Created;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        Touch(now);
    }

    /// <summary>Re-arms an Unknown or stale Pending claim for a new settle-then-write attempt.</summary>
    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        AttemptStartedAt = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
