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
/// Lists the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService subscriptionService, CancellationToken ct) =>
            {
                var userName = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(userName))
                    return Results.Unauthorized();

                return await HandleAsync(new ListMySubscriptionsRequest { UserName = userName }, subscriptionService, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionService subscriptionService, CancellationToken ct)
    {
        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(request.UserName, ct);
        var response = new ListMySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
