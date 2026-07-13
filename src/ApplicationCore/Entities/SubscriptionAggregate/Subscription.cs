using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

// Read-model view of a billing-provider subscription for an eShopOnWeb user.
// Not persisted via EF Core: mapping/identity is kept stateless and resolved
// from the provider on demand (idempotent on the user reference, see plan §8).
public class Subscription : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Subscription() { }
#pragma warning restore CS8618

    public Subscription(
        int billingSubscriptionId,
        string userReference,
        int billingCustomerId,
        string productHandle,
        string productName,
        int priceInCents,
        string state,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        bool cancelAtEndOfPeriod)
    {
        Id = billingSubscriptionId;
        UserReference = userReference;
        BillingCustomerId = billingCustomerId;
        ProductHandle = productHandle;
        ProductName = productName;
        PriceInCents = priceInCents;
        State = state;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        CancelAtEndOfPeriod = cancelAtEndOfPeriod;
    }

    public string UserReference { get; private set; }
    public int BillingCustomerId { get; private set; }
    public string ProductHandle { get; private set; }
    public string ProductName { get; private set; }
    public int PriceInCents { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; private set; }
    public DateTimeOffset? NextAssessmentAt { get; private set; }
    public bool CancelAtEndOfPeriod { get; private set; }
}
