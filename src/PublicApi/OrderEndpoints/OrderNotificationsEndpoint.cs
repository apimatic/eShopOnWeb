using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each
/// message. Shopper-scoped: the order must belong to the caller, else it is not found.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orderService = http.RequestServices.GetRequiredService<IApiOrderService>();
        var notificationService = http.RequestServices.GetRequiredService<ISmsNotificationService>();

        // A shopper must never see another's order.
        var order = await orderService.GetOrderForBuyerAsync(buyerId, orderId, http.RequestAborted);
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, refresh: true, http.RequestAborted);
        var response = new OrderNotificationsResponse(System.Guid.NewGuid())
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
