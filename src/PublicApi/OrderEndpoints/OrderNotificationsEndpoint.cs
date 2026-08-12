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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for an order and what became of each message. Each entry carries its own
/// notificationId (what the operator endpoints act on). Accessible to the order's owner or an operator.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orderRepository = httpContext.RequestServices.GetRequiredService<IReadRepository<Order>>();
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return Results.NotFound();

        // Owner or operator only; another shopper's order is simply not found for this caller.
        if (!httpContext.User.IsAdministrator() && order.BuyerId != buyerId)
            return Results.NotFound();

        var notifications = await notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(orderId), cancellationToken);
        await notificationService.RefreshDeliveryStatusesAsync(notifications, cancellationToken);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
