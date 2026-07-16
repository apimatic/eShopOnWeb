using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A read-model snapshot of a customer's subscription, hydrated live from the billing provider on
/// every request (see §8: persistence is stateless, idempotent on the eShopOnWeb user reference).
/// Id mirrors the provider's own subscription id.
/// </summary>
public class Subscription : BaseEntity, IAggregateRoot
{
    public Subscription(
        int id,
        int customerId,
        string customerReference,
        string productHandle,
        string productName,
        long priceInCents,
        SubscriptionState state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod)
    {
        Id = id;
        CustomerId = customerId;
        CustomerReference = customerReference;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    public int CustomerId { get; }
    public string CustomerReference { get; }
    public string ProductHandle { get; }
    public string ProductName { get; }
    public long PriceInCents { get; }
    public SubscriptionState State { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public bool CancelAtEndOfPeriod { get; }
}
