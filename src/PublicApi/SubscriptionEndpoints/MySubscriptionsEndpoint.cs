using System.Security.Claims;
using System.Threading;
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
/// Lists the subscriptions of the authenticated user
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptions, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptions, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptions, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var list = await subscriptions.ListMySubscriptionsAsync(userName, cancellationToken);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(list.Subscriptions);
        response.IsTruncated = list.IsTruncated;
        return Results.Ok(response);
    }
}
