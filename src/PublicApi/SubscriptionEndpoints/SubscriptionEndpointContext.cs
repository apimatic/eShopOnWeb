using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The single "dependency" (per <c>IEndpoint&lt;TResponse, TRequest, TDependency&gt;</c>) shared by every
/// authenticated subscription endpoint: the use-case service plus the calling user's identity, resolved
/// once in <c>AddRoute</c> from DI and the JWT <see cref="System.Security.Claims.ClaimsPrincipal"/> and
/// handed to <c>HandleAsync</c> so it stays a single, directly testable method.
/// </summary>
public record SubscriptionEndpointContext(ISubscriptionService SubscriptionService, string UserReference, bool IsAdmin);
