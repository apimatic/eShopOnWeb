using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns what was sent for one of the caller's orders, and what became of each message. Ownership is enforced:
/// another shopper's order is reported as not found. Each entry carries its own notificationId — what the
/// operator endpoints (resend, content disposal) act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository, IRepository<Notification> notificationRepository,
             ISmsProvider smsProvider, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, orderRepository, notificationRepository, smsProvider, user, ct);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int orderId,
        IReadRepository<Order> orderRepository,
        IRepository<Notification> notificationRepository,
        ISmsProvider smsProvider,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.UserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            // Don't reveal the existence of another shopper's order.
            return Results.NotFound();
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await NotificationStatusRefresher.RefreshAsync(notifications, smsProvider, notificationRepository, ct);

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
