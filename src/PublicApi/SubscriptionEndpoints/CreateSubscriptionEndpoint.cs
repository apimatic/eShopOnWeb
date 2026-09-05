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
/// Subscribes the authenticated buyer to a plan. Ensures a Maxio customer exists for the
/// buyer and enrolls them - calling this more than once for the same buyer/plan (e.g. a
/// double-click) returns the existing subscription rather than creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioSubscriptionService subscriptionService) =>
            {
                var buyerId = user.Identity!.Name!;
                request.BuyerId = buyerId;
                request.BuyerEmail = buyerId;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("PlanHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(request.BuyerId, request.BuyerEmail, request.PlanHandle);
        response.Subscription = SubscriptionMapping.ToDto(subscription);

        return Results.Ok(response);
    }
}
