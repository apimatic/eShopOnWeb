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
/// List Maxio subscriptions for the authenticated shopper
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billingService, CurrentShopperResolver currentShopper, HttpContext httpContext) =>
            {
                return await HandleAsync(billingService, currentShopper, httpContext);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService)
        => HandleAsync(billingService, currentShopper: null, httpContext: null);

    private static async Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        CurrentShopperResolver? currentShopper,
        HttpContext? httpContext)
    {
        if (httpContext?.User is null || currentShopper is null)
        {
            return Results.Unauthorized();
        }

        var userId = await currentShopper.ResolveUserIdAsync(httpContext.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billingService.ListUserSubscriptionsAsync(userId);
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.ToDto));
        return Results.Ok(response);
    }
}
