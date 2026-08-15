using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: ensures a single billing customer
/// exists for the user and will not create a duplicate live subscription to the same plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                // Identity always comes from the token, never the request body.
                request.RequesterEmail = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.RequesterEmail))
        {
            return Results.Problem("Could not determine the authenticated user.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem("A 'planHandle' is required to subscribe.", statusCode: StatusCodes.Status400BadRequest);
        }

        var subscribeRequest = new SubscribeRequest(
            userReference: request.RequesterEmail,
            email: request.RequesterEmail,
            planHandle: request.PlanHandle);

        var subscription = await billingService.SubscribeAsync(subscribeRequest);
        response.Subscription = subscription.ToDto();

        return Results.Ok(response);
    }
}
