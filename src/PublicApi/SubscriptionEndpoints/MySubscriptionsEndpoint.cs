using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ISubscriptionService svc, HttpContext ctx) =>
        {
            var userName = ctx.User.Identity?.Name ?? "";
            if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
            var result = await svc.GetMySubscriptionsAsync(userName);
            if (result == null) return Results.Ok(new object[] {});
            var errorProp = result.GetType().GetProperty("error");
            if (errorProp != null) return Results.BadRequest(result);
            return Results.Ok(result);
        })
        .Produces<SubscriptionDto[]>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.Ok();
    }
}
