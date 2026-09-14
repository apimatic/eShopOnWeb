using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Outcome of an (idempotent) subscribe call.</summary>
public sealed record SubscriptionEnrollment(SubscriptionDto Subscription, bool CreatedNew);
