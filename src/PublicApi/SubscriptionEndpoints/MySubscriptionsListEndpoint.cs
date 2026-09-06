using System.Linq;
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
/// Lists the subscriptions held by the authenticated shopper, newest first.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionService, CancellationToken>
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

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(userName, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));
        response.HasActiveSubscription = subscriptions.Any(s => s.IsCurrent);

        return Results.Ok(response);
    }
}
