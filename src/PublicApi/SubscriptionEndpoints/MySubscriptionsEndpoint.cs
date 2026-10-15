using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// List the authenticated user's subscriptions with their current state
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var userName = SubscriptionResults.UserName(user);
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        try
        {
            var items = await subscriptionService.ListMySubscriptionsAsync(userName, cancellationToken);
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(items.Select(SubscriptionResults.ToDto));
            return Results.Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionResults.Failure(ex);
        }
    }
}
