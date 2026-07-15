using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A read model of a billing-provider subscription, hydrated from <see cref="IBillingClient"/> on
/// every call (plan.md §8 decision: stateless mapping, idempotent on the user reference — no local
/// persistence/EF migration). <see cref="Id"/> is the provider's subscription id.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(
        int id,
        string ownerReference,
        string productHandle,
        int productId,
        long productPriceInCents,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? createdAt)
    {
        Id = id;
        OwnerReference = Guard.Against.NullOrEmpty(ownerReference, nameof(ownerReference));
        ProductHandle = Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));
        ProductId = productId;
        ProductPriceInCents = productPriceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CreatedAt = createdAt;
    }

    /// <summary>The stable eShopOnWeb user reference (email/username) this subscription's billing-provider customer was created with.</summary>
    public string OwnerReference { get; }
    public string ProductHandle { get; }
    public int ProductId { get; }
    public long ProductPriceInCents { get; }
    public SubscriptionState State { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>States in which a subscription is considered "already subscribed" for UC1 duplicate-detection purposes.</summary>
    public bool IsActiveOrTrialing => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanChangePlan => State is SubscriptionState.Active or SubscriptionState.Trialing;

    public bool CanPause => State is SubscriptionState.Active or SubscriptionState.Trialing;

    // Verified live: SubscriptionStatus.PauseSubscription transitions the subscription to OnHold, not
    // Paused, on this site's configuration - both are accepted here since either is a legitimate
    // provider-reported "currently paused" state.
    public bool CanResume => State is SubscriptionState.Paused or SubscriptionState.OnHold;

    public bool CanCancel => State is not (SubscriptionState.Canceled or SubscriptionState.Expired);

    public bool CanReactivate => State is SubscriptionState.Canceled or SubscriptionState.Unpaid or SubscriptionState.TrialEnded;
}
