using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Per-request dependency bag for subscription endpoints that need both the (scoped)
/// <see cref="ISubscriptionService"/> and the caller's identity — bundled into one type so it can
/// flow through <c>IEndpoint&lt;TResult, TRequest, TDep&gt;</c>'s single dependency slot without
/// capturing per-request state on the endpoint instance itself (endpoints are singletons).
/// </summary>
public record SubscriptionEndpointContext(ISubscriptionService SubscriptionService, ClaimsPrincipal User);
