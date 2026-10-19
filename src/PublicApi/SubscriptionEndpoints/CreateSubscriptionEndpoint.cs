using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated buyer to a recurring plan. Idempotent per buyer: repeating the
/// request returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext context, SubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, context, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext context, SubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { StatusCode = StatusCodes.Status400BadRequest, Message = "planHandle is required." });
        }

        var buyerId = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(buyerId);
        var buyer = new BuyerProfile(buyerId, user?.Email ?? buyerId, request.FirstName, request.LastName);

        var outcome = await subscriptionService.SubscribeAsync(buyer, request.PlanHandle, context.RequestAborted);

        if (outcome.Result is null)
        {
            response.Status = "pending";
            response.Message = "Your subscription is being confirmed with the billing provider. Check GET /api/my-subscriptions shortly.";
            return Results.Accepted("api/my-subscriptions", response);
        }

        response.Subscription = SubscriptionDto.From(outcome.Result.Subscription);
        if (outcome.Result.Created)
        {
            response.Status = "created";
            return Results.Created("api/my-subscriptions", response);
        }

        response.Status = "existing";
        response.Message = "You are already subscribed to this plan.";
        return Results.Ok(response);
    }
}
