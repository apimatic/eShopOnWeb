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
/// Subscribes the authenticated shopper to a plan. Ensures a billing customer exists
/// for the user (idempotent) and enrolls them, then confirms the plan, price, state,
/// and next billing date. A double-click does not create a second customer or a
/// duplicate active subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, user, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService)
    {
        var subscriber = SubscriberFactory.FromPrincipal(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required. Provide the handle of a plan from /api/subscription-plans." });
        }

        SubscriptionDetails details;
        try
        {
            details = await billingService.SubscribeAsync(subscriber, planHandle);
        }
        catch (UnknownSubscriptionPlanException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = details.ToDto(),
            AlreadyExisted = details.AlreadyExisted,
            Message = details.AlreadyExisted
                ? $"You are already subscribed to {details.PlanName} ({details.State})."
                : $"Subscribed to {details.PlanName} ({details.State})."
        };

        return details.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created("/api/my-subscriptions", response);
    }
}
