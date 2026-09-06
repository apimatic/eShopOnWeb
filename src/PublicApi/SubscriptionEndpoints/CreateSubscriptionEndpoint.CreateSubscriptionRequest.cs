using System.Security.Claims;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of a subscribe request.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Optional
    /// only when the deployment configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Everything the subscribe endpoint needs from the HTTP request, assembled at the route so the handler
/// itself has no dependency on <c>HttpContext</c>.
/// </summary>
/// <param name="Body">The parsed request body.</param>
/// <param name="Caller">The authenticated caller; the only source of the shopper's identity.</param>
/// <param name="IdempotencyKey">Value of the optional <c>Idempotency-Key</c> request header.</param>
/// <param name="CancellationToken">Cancelled when the client disconnects.</param>
public sealed record SubscribeCommand(
    CreateSubscriptionRequest? Body,
    ClaimsPrincipal Caller,
    string? IdempotencyKey,
    CancellationToken CancellationToken);
