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
/// Subscribes the authenticated user to a plan
/// </summary>
public class SubscribeEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, service, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new SubscriptionException(400, "planHandle is required.");
        }

        var result = await service.SubscribeAsync(userName, request.PlanHandle.Trim(), cancellationToken);
        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            AlreadySubscribed = result.AlreadySubscribed,
        };
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
