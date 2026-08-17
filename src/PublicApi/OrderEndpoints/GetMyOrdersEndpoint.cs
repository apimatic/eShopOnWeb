using System.Linq;
using System.Security.Claims;
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

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
                await HandleAsync(user, service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(buyerId);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
