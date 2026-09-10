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
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a billing customer
/// exists for the user and returns the existing subscription when they are already enrolled
/// in the requested plan, so a double-click never creates duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                // Identity always comes from the token, never the request body.
                request.Subscriber = user.ToSubscriber();
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle);
        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
