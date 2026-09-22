using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record of a buyer's enrollment in a Maxio subscription plan. Maxio Advanced Billing is the
/// system of record; this row exists to (a) carry the concurrency duplicate-claim — a UNIQUE index on
/// (<see cref="BuyerId"/>, <see cref="PlanHandle"/>) that keeps a double-click from creating two
/// subscriptions — and (b) track the write so an ambiguous provider outcome can be reconciled. It is
/// not the source of truth for reads.
/// </summary>
public class BuyerSubscription : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BuyerSubscription() { }
#pragma warning restore CS8618

    public BuyerSubscription(string buyerId, string planHandle, string subscriptionReference)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PlanHandle = Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        SubscriptionReference = Guard.Against.NullOrEmpty(subscriptionReference, nameof(subscriptionReference));
        Status = BillingRecordStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The eShopOnWeb user id (ASP.NET Identity user id) — the Maxio customer reference.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The Maxio product (plan) API handle this claim is for.</summary>
    public string PlanHandle { get; private set; }

    /// <summary>Deterministic Maxio subscription reference used to reconcile an ambiguous write.</summary>
    public string SubscriptionReference { get; private set; }

    public int? MaxioCustomerId { get; private set; }
    public int? MaxioSubscriptionId { get; private set; }

    /// <summary>Local claim lifecycle — see <see cref="BillingRecordStatus"/>.</summary>
    public BillingRecordStatus Status { get; private set; }

    /// <summary>The last-known Maxio subscription state (e.g. "active"), captured for diagnostics.</summary>
    public string? ProviderState { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetCustomer(int customerId)
    {
        MaxioCustomerId = customerId;
        Touch();
    }

    public void MarkProvisioned(int? subscriptionId, string? providerState)
    {
        MaxioSubscriptionId = subscriptionId;
        ProviderState = providerState;
        Status = BillingRecordStatus.Provisioned;
        Touch();
    }

    public void MarkFailed()
    {
        Status = BillingRecordStatus.Failed;
        Touch();
    }

    /// <summary>
    /// The provider write was sent but its outcome could not be confirmed (transport failure / unreadable
    /// response). Distinct from <see cref="MarkFailed"/>: the subscription may in fact exist.
    /// </summary>
    public void MarkOutcomeUnknown()
    {
        Status = BillingRecordStatus.Unknown;
        Touch();
    }

    /// <summary>Reuse this row for a fresh attempt after a prior Failed/Unknown outcome.</summary>
    public void BeginAttempt()
    {
        Status = BillingRecordStatus.Pending;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
