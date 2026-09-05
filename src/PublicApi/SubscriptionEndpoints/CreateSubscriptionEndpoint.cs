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
/// Subscribes the calling shopper to a Maxio plan. Ensures a Maxio customer exists for them
/// (idempotent) and enrolls them (idempotent) - a double submit never creates duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                var appUser = await userManager.FindByNameAsync(user.Identity!.Name!);
                if (appUser is null)
                {
                    return Results.Unauthorized();
                }

                request.UserReference = appUser.Id;
                request.UserEmail = appUser.Email ?? appUser.UserName!;

                return await HandleAsync(request, maxioSubscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("planHandle is required.");
        }

        var subscription = await maxioSubscriptionService.SubscribeAsync(request.UserReference, request.UserEmail, request.PlanHandle);

        response.Subscription = new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            NextBillingDate = subscription.NextBillingDate,
            CreatedAt = subscription.CreatedAt
        };

        return Results.Ok(response);
    }
}
