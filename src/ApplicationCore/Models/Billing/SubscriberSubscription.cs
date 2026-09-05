using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A buyer's subscription as it currently stands in Maxio - Maxio is the system of record, this
/// is never persisted locally.
/// </summary>
public record SubscriberSubscription(
    int MaxioSubscriptionId,
    int MaxioCustomerId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);
