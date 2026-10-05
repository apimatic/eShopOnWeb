using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The result of enrolling an eShopOnWeb user into a subscription
/// hosted in the external billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionEnrollment
{
    public int SubscriptionId { get; private set; }
    public string Reference { get; private set; }
    public string CustomerId { get; private set; }
    public string PlanHandle { get; private set; }
    public string PlanName { get; private set; }
    public long PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? NextBillingDate { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>
    /// True when the subscription already existed in the billing system
    /// (idempotent re-subscribe) rather than being newly created.
    /// </summary>
    public bool AlreadySubscribed { get; private set; }

    public SubscriptionEnrollment(int subscriptionId, string reference, string customerId,
        string planHandle, string planName, long priceInCents, string state,
        DateTimeOffset? nextBillingDate, DateTimeOffset? activatedAt,
        DateTimeOffset createdAt, bool alreadySubscribed)
    {
        SubscriptionId = subscriptionId;
        Reference = reference;
        CustomerId = customerId;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        State = state;
        NextBillingDate = nextBillingDate;
        ActivatedAt = activatedAt;
        CreatedAt = createdAt;
        AlreadySubscribed = alreadySubscribed;
    }
}