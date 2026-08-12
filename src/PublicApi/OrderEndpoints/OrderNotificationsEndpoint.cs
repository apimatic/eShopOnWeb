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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }

    // Each entry carries its own notificationId — that is what the operator endpoints act on.
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for an order, and what became of each message. Readable by the order's owner
/// (their own data) or by an operator (administrator), who acts on the returned notificationIds.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository,
                IReadRepository<Notification> notificationRepository, INotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, user, orderRepository, notificationRepository, notificationService, cancellationToken);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository,
        IReadRepository<Notification> notificationRepository, INotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        // A shopper may only see their own order; an operator (admin) may look up any.
        if (order.BuyerId != buyerId && !user.IsAdministrator())
        {
            return Results.NotFound();
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await notificationService.RefreshDeliveryStateAsync(notifications, cancellationToken);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.OrderBy(n => n.CreatedDate).Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
