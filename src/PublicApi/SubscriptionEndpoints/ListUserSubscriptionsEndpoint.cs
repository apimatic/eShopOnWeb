using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionManager>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionManager manager, HttpContext httpContext) =>
            {
                return await HandleAsync(manager, httpContext);
            })
           .Produces<ListUserSubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionManager manager)
    {
        return Results.Ok();
    }

    private async Task<IResult> HandleAsync(ISubscriptionManager manager, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var subscriptions = await manager.GetUserSubscriptionsAsync(userId);
            return Results.Ok(new ListUserSubscriptionsResponse
            {
                Subscriptions = new List<UserSubscriptionDto>(subscriptions)
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListUserSubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
