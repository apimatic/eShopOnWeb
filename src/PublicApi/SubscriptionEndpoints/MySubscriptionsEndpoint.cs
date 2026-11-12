using System.Collections.Generic;
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
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionService service, CancellationToken ct) =>
                await HandleAsync(user, service, ct))
           .Produces<MySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionService service, CancellationToken ct)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
            return Results.Unauthorized();

        var (items, truncated) = await service.ListMySubscriptionsAsync(userName, ct);
        var response = new MySubscriptionsResponse { Truncated = truncated };
        response.Subscriptions.AddRange(items);
        return Results.Ok(response);
    }
}

public class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    /// <summary>True when more subscriptions exist than were returned.</summary>
    public bool Truncated { get; set; }
}
