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
/// Lists the authenticated shopper's subscriptions. Returns an empty list when the shopper has
/// no billing customer yet.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, string, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(user.Identity?.Name ?? string.Empty, billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string userReference, ISubscriptionBillingService billingService)
    {
        var response = new MySubscriptionsResponse();

        if (string.IsNullOrWhiteSpace(userReference))
        {
            return Results.Problem("Could not determine the authenticated user.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var subscriptions = await billingService.ListSubscriptionsForUserAsync(userReference);
        response.Subscriptions.AddRange(subscriptions.Select(s => s.ToDto()));

        return Results.Ok(response);
    }
}
