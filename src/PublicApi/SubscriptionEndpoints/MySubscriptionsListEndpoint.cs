using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling (JWT-authenticated) user's Maxio subscriptions.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(principal, subscriptionAppService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService)
        => await HandleAsync(principal, subscriptionAppService, default);

    private async Task<IResult> HandleAsync(ClaimsPrincipal principal, ISubscriptionAppService subscriptionAppService, CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await subscriptionAppService.GetCurrentUserSubscriptionsAsync(principal, cancellationToken));
        return Results.Ok(response);
    }
}
