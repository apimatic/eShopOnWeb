using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan: ensures a Maxio customer exists for them
/// (idempotent) and enrolls them in the requested plan. Subscribing twice to the same plan (e.g. a
/// double-click) returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                var username = claimsPrincipal.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Results.Unauthorized();
                }

                var user = await userManager.FindByNameAsync(username);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                request.UserReference = user.Id;
                request.Email = user.Email ?? username;

                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await maxioSubscriptionService.SubscribeAsync(request.UserReference, request.Email, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Plan?.Handle ?? request.PlanHandle,
            PlanName = subscription.Plan?.Name ?? string.Empty,
            Price = (subscription.Plan?.PriceInCents ?? 0) / 100m,
            State = subscription.State,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };

        return Results.Ok(response);
    }
}
