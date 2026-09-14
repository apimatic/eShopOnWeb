using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the current Maxio subscriptions owned by the authenticated shopper.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioSubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(principal, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IMaxioSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var shopperEmail = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperEmail))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(shopperEmail, cancellationToken);

        var response = new ListMySubscriptionsResponse
        {
            Subscriptions = SubscriptionDtoMapper.ToSubscriptionDtos(subscriptions)
        };

        return Results.Ok(response);
    }
}
