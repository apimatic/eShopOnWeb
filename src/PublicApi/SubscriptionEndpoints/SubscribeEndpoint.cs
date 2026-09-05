using System.Security.Claims;
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
/// Subscribes the caller to a Maxio subscription plan. Idempotent: resolving the same user
/// against the same plan more than once returns the existing subscription rather than
/// creating a duplicate.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                request.Username = user.Identity!.Name!;
                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<SubscribeResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await maxioSubscriptionService.SubscribeAsync(request.Username, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
        };

        return Results.Ok(response);
    }
}
