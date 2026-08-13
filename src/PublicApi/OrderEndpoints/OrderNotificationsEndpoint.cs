using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}

/// <summary>
/// What was sent for one of the caller's own orders, and what became of each message. Each entry
/// carries its own notificationId — the identifier the operator endpoints act on. Scoped to the
/// caller's own order; another shopper's order is reported as not found.
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
        var ownerId = CallerIdentity.GetOwnerId(http.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var orderRepository = http.RequestServices.GetRequiredService<IReadRepository<Order>>();
        var notificationRepository = http.RequestServices.GetRequiredService<IRepository<SmsNotification>>();
        var notificationService = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.GetByIdAsync(orderId, http.RequestAborted);
        if (order is null || order.BuyerId != ownerId)
            return Results.NotFound();

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), http.RequestAborted);
        await notificationService.RefreshDeliveryStateAsync(notifications, http.RequestAborted);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationView.From).ToList()
        };
        return Results.Ok(response);
    }
}
