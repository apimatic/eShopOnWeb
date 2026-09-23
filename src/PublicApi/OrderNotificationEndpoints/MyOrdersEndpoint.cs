using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record MyOrdersResponse(IReadOnlyList<OrderSummary> Orders);

/// <summary>The signed-in shopper's orders, each showing where its notifications got to (provider state refreshed).</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string?, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(user.FindFirstValue(ClaimTypes.Name), service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string? buyerId, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(buyerId, default);
        return Results.Ok(new MyOrdersResponse(orders));
    }
}
