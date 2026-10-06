using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Outcome of a subscribe call. <see cref="Created"/> is false when the request was
/// recognized as a replay of an earlier enrollment (idempotency).
/// </summary>
public record SubscriptionSignupResult(BillingSubscription Subscription, bool Created);
