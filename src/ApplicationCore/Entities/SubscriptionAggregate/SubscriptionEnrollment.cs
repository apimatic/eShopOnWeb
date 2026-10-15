using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's enrollment in a recurring subscription held by the billing system (Maxio).
/// Keyed by the buyer id, so the store refuses a second enrollment for the same shopper: inserting this
/// row is the claim that stops a double-submitted subscribe from reaching the billing system twice.
/// </summary>
public class SubscriptionEnrollment : IAggregateRoot
{
    public string BuyerId { get; private set; }
    public SubscriptionEnrollmentStatus Status { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>The billing customer's reference: deterministic per buyer, sent on customer create.</summary>
    public string CustomerReference { get; private set; }

    /// <summary>The subscription's reference: generated per attempt, sent on subscription create and used to find it again.</summary>
    public string SubscriptionReference { get; private set; }

    public int? BillingCustomerId { get; private set; }
    public int? BillingSubscriptionId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token: changes on every update, so two racing updates cannot both win.</summary>
    public Guid Version { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionEnrollment() { }

    public SubscriptionEnrollment(string buyerId, string planHandle, string customerReference, string subscriptionReference, DateTimeOffset now)
    {
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        CustomerReference = Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        Status = SubscriptionEnrollmentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// A pending enrollment nobody has touched for <paramref name="staleAfter"/> belongs to a request that
    /// is no longer running; its outcome has to be settled against the billing system before it is reused.
    /// </summary>
    public bool NeedsSettling(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == SubscriptionEnrollmentStatus.Unknown ||
        (Status == SubscriptionEnrollmentStatus.Pending && now - UpdatedAt > staleAfter);

    public void RecordCustomer(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        Touch(now);
    }

    public void MarkActive(int billingSubscriptionId, string planHandle, DateTimeOffset now)
    {
        BillingSubscriptionId = billingSubscriptionId;
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        Status = SubscriptionEnrollmentStatus.Active;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = SubscriptionEnrollmentStatus.Unknown;
        Touch(now);
    }

    /// <summary>
    /// Re-opens a finished enrollment (its subscription ended) for a new subscribe attempt. Saved with the
    /// concurrency token, so only one of two racing re-subscribes gets through.
    /// </summary>
    public void Restart(string planHandle, string subscriptionReference, DateTimeOffset now)
    {
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        SubscriptionReference = Guard.Against.NullOrWhiteSpace(subscriptionReference, nameof(subscriptionReference));
        BillingSubscriptionId = null;
        Status = SubscriptionEnrollmentStatus.Pending;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
