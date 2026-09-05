using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's own Maxio subscriptions
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
            {
                return await HandleAsync(user, subscriptionBillingService);
            })
            .Produces<MySubscriptionsListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService)
    {
        var customerReference = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsListResponse();

        var subscriptions = await subscriptionBillingService.GetCustomerSubscriptionsAsync(customerReference);
        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.PriceInCents.HasValue ? s.PriceInCents.Value / 100m : null,
            State = s.State,
            NextBillingDate = s.NextBillingDate
        }).ToList();

        return Results.Ok(response);
    }
}
