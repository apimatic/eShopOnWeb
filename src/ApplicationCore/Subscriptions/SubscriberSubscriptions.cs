using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Everything the billing system knows about one shopper's subscriptions.
/// </summary>
/// <param name="CustomerReference">
/// The reference under which this shopper is registered in the billing system. Always populated,
/// even when the shopper has no customer record yet, so callers can see the key that would be used.
/// </param>
/// <param name="CustomerId">
/// The billing-system customer id, or null when no customer has been created for this shopper yet.
/// </param>
/// <param name="Subscriptions">The shopper's subscriptions, newest first. Empty when there are none.</param>
public sealed record SubscriberSubscriptions(
    string CustomerReference,
    int? CustomerId,
    IReadOnlyList<CustomerSubscription> Subscriptions);
