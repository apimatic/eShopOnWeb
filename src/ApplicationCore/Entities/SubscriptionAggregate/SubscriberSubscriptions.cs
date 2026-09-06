using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A shopper's billing footprint: their provider customer record (if one exists yet) and every
/// subscription attached to it.
/// </summary>
/// <param name="Customer">Null when the shopper has never subscribed, so no provider customer exists.</param>
/// <param name="Subscriptions">All subscriptions on the customer, newest first.</param>
public record SubscriberSubscriptions(BillingCustomer? Customer, IReadOnlyList<CustomerSubscription> Subscriptions);
