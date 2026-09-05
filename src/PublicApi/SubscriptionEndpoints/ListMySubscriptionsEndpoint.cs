using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the current eShopOnWeb user's Maxio subscriptions (empty if they haven't subscribed yet).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(httpContext, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IMaxioSubscriptionService subscriptionService)
    {
        var user = await CurrentUserResolver.GetCurrentUserAsync(httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscriptionsAsync(user.Id);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.SubscriptionId,
                State = s.State,
                NextBillingAt = s.NextBillingAt,
                Plan = new SubscriptionPlanDto
                {
                    Handle = s.Plan.Handle,
                    Name = s.Plan.Name,
                    Price = s.Plan.Price,
                    IntervalCount = s.Plan.IntervalCount,
                    IntervalUnit = s.Plan.IntervalUnit
                }
            }).ToList()
        };

        return Results.Ok(response);
    }
}
