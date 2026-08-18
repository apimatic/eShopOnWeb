using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(System.Guid correlationId) : base(correlationId) { }

    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orderService = http.RequestServices.GetRequiredService<IApiOrderService>();
        var notificationService = http.RequestServices.GetRequiredService<ISmsNotificationService>();

        var orders = await orderService.GetOrdersForBuyerAsync(buyerId, http.RequestAborted);
        var orderIds = orders.Select(o => o.Id).ToList();
        var notificationsByOrder = await notificationService.GetNotificationsForOrdersAsync(orderIds, refresh: true, http.RequestAborted);

        var response = new MyOrdersResponse(System.Guid.NewGuid())
        {
            Orders = orders.Select(order =>
            {
                notificationsByOrder.TryGetValue(order.Id, out var list);
                var dtos = (list ?? new List<SmsNotification>()).Select(NotificationDto.From);
                return OrderDto.From(order, dtos);
            }).ToList()
        };

        return Results.Ok(response);
    }
}
