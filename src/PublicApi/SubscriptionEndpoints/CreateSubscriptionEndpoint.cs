using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, ISubscriptionService svc, HttpContext ctx) =>
        {
            var userName = ctx.User.Identity?.Name ?? "";
            if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
            var result = await svc.SubscribeAsync(userName, req.ProductHandle);
            if (result == null) return Results.BadRequest("No result");
            var errorProp = result.GetType().GetProperty("error");
            if (errorProp != null) return Results.BadRequest(result);
            return Results.Ok(result);
        })
        .Produces<CreateSubscriptionResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        return Results.Ok();
    }
}
