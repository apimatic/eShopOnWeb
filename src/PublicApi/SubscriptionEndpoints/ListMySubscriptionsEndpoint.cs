using System.Linq;
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
/// Lists the authenticated eShopOnWeb user's Maxio subscriptions. Returns an empty list if
/// the user has never subscribed (no matching Maxio customer yet).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioSubscriptionService maxioService, UserManager<ApplicationUser> userManager, HttpContext httpContext) =>
            {
                var user = await CurrentUserAccessor.GetCurrentUserAsync(httpContext.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var request = new ListMySubscriptionsRequest { UserId = CurrentUserAccessor.ToCustomerProfile(user).Reference };
                return await HandleAsync(request, maxioService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioSubscriptionService maxioService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await maxioService.GetSubscriptionsAsync(request.UserId);
        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.PriceInCents / 100m,
            State = s.State,
            NextBillingDate = s.NextBillingDate,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
