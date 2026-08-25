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
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService) =>
            {
                // The JWT carries the username in ClaimTypes.Name (see IdentityTokenClaimService).
                var username = user.Identity?.Name;
                var appUser = username == null ? null : await userManager.FindByNameAsync(username);
                if (appUser == null)
                {
                    return Results.Unauthorized();
                }

                request.UserId = appUser.Id;
                request.Email = appUser.Email ?? appUser.UserName ?? string.Empty;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscription = await billingService.SubscribeAsync(request.UserId, request.Email, request.PlanHandle);
        response.Subscription = Map(subscription);

        return Results.Ok(response);
    }

    internal static SubscriptionDto Map(ApplicationCore.Models.CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Currency = subscription.Currency,
            NextBillingDate = subscription.NextBillingDate,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }
}
