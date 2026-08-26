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
/// List the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                var username = CreateSubscriptionEndpoint.UsernameOf(user);
                if (username is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(billingService, username);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
        => HandleAsync(billingService, string.Empty);

    private async Task<IResult> HandleAsync(ISubscriptionBillingService billingService, string username)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.ListMySubscriptionsAsync(username);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            ProductHandle = s.ProductHandle,
            ProductName = s.ProductName,
            State = s.State,
            Price = s.Price,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingDate = s.NextBillingDate
        }));

        return Results.Ok(response);
    }
}
