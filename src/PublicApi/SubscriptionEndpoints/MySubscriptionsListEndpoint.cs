using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions (GET /api/my-subscriptions).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(http, subscriptionService);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsListResponse();

        var loginName = http.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(loginName, http.RequestAborted);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionSummaryDto
        {
            SubscriptionId = s.Id,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceAmount = s.PriceAmount,
            Currency = s.Currency,
            State = s.State,
            NextBillingDate = s.NextBillingDate
        }));

        return Results.Ok(response);
    }
}
