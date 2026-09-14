using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MySubscriptionEndpoints;

/// <summary>
/// Lists the signed-in caller's subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly ISubscriptionService _subscriptionService;

    public MySubscriptionListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(user, ct);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("MySubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var email = ResolveEmail(user);
        if (email is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(email, ct);

        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionDto.From(subscription));
        }

        return Results.Ok(response);
    }

    private static string? ResolveEmail(ClaimsPrincipal user)
    {
        var email = user.FindFirstValue(ClaimTypes.Name)?.Trim();
        return string.IsNullOrEmpty(email) ? null : email.ToLowerInvariant();
    }
}
