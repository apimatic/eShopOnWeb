using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// What a subscription endpoint needs besides its request: the caller (resolved from the JWT, null when the
/// token names no eShopOnWeb user), the billing service and the request's cancellation.
/// </summary>
public sealed record SubscriptionCallContext(BillingUser? User, ISubscriptionBillingService Billing, CancellationToken CancellationToken);
