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
/// Lists the calling shopper's subscriptions, read live from Maxio
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
        HandleAsync(user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.ListSubscriptionsAsync(buyerId, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));

        return Results.Ok(response);
    }
}
