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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for one of the caller's orders and what became of each message. Scoped to the owner:
/// a shopper can only see notifications for their own order. Each entry carries its own notificationId (the
/// identifier the operator endpoints act on).
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
        var callerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(callerId)) return Results.Unauthorized();

        var ct = http.RequestAborted;
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationRepository = http.RequestServices.GetRequiredService<IRepository<Notification>>();
        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.GetByIdAsync(orderId, ct);
        // Don't leak another shopper's order: unknown or not-owned both read as not found.
        if (order is null || order.BuyerId != callerId) return Results.NotFound();

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await notifier.RefreshStatusesAsync(notifications, ct);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationView.From).ToList()
        };
        return Results.Ok(response);
    }
}
