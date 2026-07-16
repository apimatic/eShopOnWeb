using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The live state of a subscription as reported by the billing provider. eShopOnWeb does not persist
/// a local copy of this state (see plan §8 "Persistence: stateless mapping") — it is always resolved
/// from the provider via the customer reference, which keeps eShopOnWeb's view intrinsically consistent
/// with the billing system of record.
/// </summary>
public record BillingSubscription(
    int Id,
    string CustomerReference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? CurrentPeriodEndsAt,
    bool CancelAtEndOfPeriod,
    DateTimeOffset? DelayedCancelAt,
    string? NextProductHandle);
