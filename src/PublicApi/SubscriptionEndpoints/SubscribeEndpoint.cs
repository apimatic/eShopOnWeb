using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptions, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptions, user, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptions, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        // Identity comes from the token only.
        var userName = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required." });
        }

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = await subscriptions.SubscribeAsync(userName, request.PlanHandle, cancellationToken)
        };
        return Results.Created("api/my-subscriptions", response);
    }
}
