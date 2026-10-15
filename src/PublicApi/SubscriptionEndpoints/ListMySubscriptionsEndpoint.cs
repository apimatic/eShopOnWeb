using System.Linq;
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
/// Lists the authenticated shopper's subscriptions as Maxio reports them: plan, price, state and next billing date
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ShopperContext, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([AsParameters] ShopperContext shopper, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(shopper, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ShopperContext shopper, ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse();

        var account = await shopper.FindAccountAsync();
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(account.UserName!, shopper.CancellationToken);

        response.Subscriptions.AddRange(subscriptions.Subscriptions.Select(SubscriptionDto.FromModel));
        response.EnrollmentStatus = subscriptions.EnrollmentStatus?.ToString();
        response.Truncated = subscriptions.Truncated;

        return Results.Ok(response);
    }
}
