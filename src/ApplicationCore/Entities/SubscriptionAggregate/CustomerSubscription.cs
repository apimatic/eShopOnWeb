using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A customer's subscription as reported by the billing provider, normalised into
/// provider-agnostic terms.
/// </summary>
public class CustomerSubscription
{
    public CustomerSubscription(int id,
        SubscriptionStatus status,
        string? providerState,
        int? customerId,
        string? customerReference,
        string? planHandle,
        string? planName,
        long? planPriceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? activatedAt,
        DateTimeOffset? canceledAt,
        DateTimeOffset? delayedCancelAt,
        bool cancelAtEndOfPeriod,
        string? nextPlanHandle)
    {
        Id = id;
        Status = status;
        ProviderState = providerState;
        CustomerId = customerId;
        CustomerReference = customerReference;
        PlanHandle = planHandle;
        PlanName = planName;
        PlanPriceInCents = planPriceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        ActivatedAt = activatedAt;
        CanceledAt = canceledAt;
        DelayedCancelAt = delayedCancelAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
        NextPlanHandle = nextPlanHandle;
    }

    public int Id { get; }

    /// <summary>The normalised state this application reasons about.</summary>
    public SubscriptionStatus Status { get; }

    /// <summary>
    /// The raw state string the provider reported. Retained so an unmodelled provider state is
    /// still surfaced to the operator instead of being silently flattened to
    /// <see cref="SubscriptionStatus.Unknown"/>.
    /// </summary>
    public string? ProviderState { get; }

    public int? CustomerId { get; }

    public string? CustomerReference { get; }

    public string? PlanHandle { get; }

    public string? PlanName { get; }

    /// <summary>Plan price in minor units (cents), as reported by the provider.</summary>
    public long? PlanPriceInCents { get; }

    /// <summary>Plan price as a currency amount, or <c>null</c> when the provider did not report one.</summary>
    public decimal? PlanPrice => PlanPriceInCents.HasValue ? PlanPriceInCents.Value / 100m : null;

    public DateTimeOffset? CurrentPeriodEndsAt { get; }

    /// <summary>The next date the provider will bill this subscription.</summary>
    public DateTimeOffset? NextAssessmentAt { get; }

    public DateTimeOffset? ActivatedAt { get; }

    public DateTimeOffset? CanceledAt { get; }

    /// <summary>Set when an end-of-period cancellation is scheduled.</summary>
    public DateTimeOffset? DelayedCancelAt { get; }

    public bool CancelAtEndOfPeriod { get; }

    /// <summary>Set when a plan change has been scheduled for the next renewal.</summary>
    public string? NextPlanHandle { get; }

    /// <summary>The next billing date to show a customer, preferring the provider's assessment date.</summary>
    public DateTimeOffset? NextBillingDate => NextAssessmentAt ?? CurrentPeriodEndsAt;

    /// <summary>True when the subscription is in a state that accrues usage and can be managed.</summary>
    public bool IsActive => Status == SubscriptionStatus.Active || Status == SubscriptionStatus.Trialing;
}
