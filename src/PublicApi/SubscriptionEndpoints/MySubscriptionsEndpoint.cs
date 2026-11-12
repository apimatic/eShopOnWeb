using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, MaxioSubscriptionService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, MaxioSubscriptionService service)
    {
        var userId = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var subscriptions = await service.ListMySubscriptionsAsync(userId, http.RequestAborted);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }
}

public class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = [];
}
