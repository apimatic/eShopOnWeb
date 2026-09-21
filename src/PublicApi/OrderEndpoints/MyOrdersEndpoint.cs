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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The signed-in shopper's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint
    : IEndpoint<IResult, string, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(buyerId, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderNotificationService service, CancellationToken ct)
    {
        var orders = await service.GetMyOrdersAsync(buyerId, ct);
        var response = new MyOrdersResponse(orders
            .Select(o => new MyOrderDto(
                o.Order.Id,
                o.Order.OrderDate,
                o.Order.Total(),
                o.Notifications.Select(NotificationMapping.ToSummary).ToList()))
            .ToList());
        return Results.Ok(response);
    }
}
