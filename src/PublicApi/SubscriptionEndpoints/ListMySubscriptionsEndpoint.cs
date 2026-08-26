using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionBillingService billingService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionBillingService billingService, ClaimsPrincipal user)
        => HandleAsync(billingService, user, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscriptionBillingService billingService, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var username = SubscriptionMapper.GetUsername(user);

        var subscriptions = await billingService.ListMySubscriptionsAsync(username, cancellationToken);

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));

        return Results.Ok(response);
    }
}
