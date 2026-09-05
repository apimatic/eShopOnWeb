using System.Linq;
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
/// Lists the calling shopper's Maxio subscriptions (empty if they have never subscribed).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, IMaxioSubscriptionService maxioSubscriptionService) =>
            {
                var appUser = await userManager.FindByNameAsync(user.Identity!.Name!);
                if (appUser is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(appUser.Id, maxioSubscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userReference, IMaxioSubscriptionService maxioSubscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await maxioSubscriptionService.GetSubscriptionsForUserAsync(userReference);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            MaxioSubscriptionId = s.MaxioSubscriptionId,
            State = s.State,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            NextBillingDate = s.NextBillingDate,
            CreatedAt = s.CreatedAt
        }));

        return Results.Ok(response);
    }
}
