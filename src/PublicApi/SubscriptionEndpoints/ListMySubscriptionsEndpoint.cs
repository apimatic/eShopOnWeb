using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal claimsPrincipal, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(claimsPrincipal, billingService);
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal claimsPrincipal, ISubscriptionBillingService billingService)
    {
        var userId = SubscriptionEndpointHelpers.GetUserId(claimsPrincipal);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.ListMySubscriptionsAsync(userId);

            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionEndpointHelpers.ToDto));

            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
