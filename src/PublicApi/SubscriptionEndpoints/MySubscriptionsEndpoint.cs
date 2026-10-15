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

public class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated shopper's subscriptions with live state from Maxio
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(service, user, ct);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(SubscriptionService service, ClaimsPrincipal user, CancellationToken ct)
    {
        var items = await service.ListMineAsync(user.Identity?.Name, ct);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = new List<SubscriptionDto>(items) });
    }
}
