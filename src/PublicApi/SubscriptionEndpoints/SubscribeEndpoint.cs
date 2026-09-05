using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists for them
/// (creating one on first use) and enrolls them - safe to call more than once for the same
/// plan (e.g. a double-click): an existing live subscription is returned rather than duplicated.
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
            .Produces((int)HttpStatusCode.BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var result = await maxioSubscriptionService.SubscribeAsync(request.Username, request.PlanHandle);
        response.Subscription = SubscriptionMapping.ToDto(result.Subscription);
        response.IsNewSubscription = result.IsNewSubscription;

        return result.IsNewSubscription
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
