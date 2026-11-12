using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (idempotent per user and plan)
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, billing, ct);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        ISubscriptionBillingService billing, CancellationToken ct)
    {
        // The caller identity comes from the token, never from the request body.
        var userName = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var result = await billing.SubscribeAsync(userName, request.PlanHandle ?? string.Empty, ct);

        var response = new SubscribeResponse(request.CorrelationId()) { Subscription = result.Subscription };
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
