using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>GET /api/my-orders — the caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint
    : IEndpoint<IResult, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(service, http))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(buyerId, http.RequestAborted);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Status = o.Order.Status.ToString(),
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(NotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
