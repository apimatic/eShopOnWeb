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

/// <summary>
/// What was sent for one of the caller's orders, and what became of each message. Scoped to the caller's
/// own order: another shopper's order is indistinguishable from a non-existent one (404).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IRepository<Notification> notificationRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderRepository.GetByIdAsync(orderId, cancellationToken);
                if (order == null || order.BuyerId != buyerId)
                {
                    return Results.NotFound();
                }

                var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
                await notificationService.RefreshStatusesAsync(notifications, cancellationToken);

                var dtos = notifications.Select(NotificationDto.From).ToList();
                return Results.Ok(dtos);
            })
            .Produces<List<NotificationDto>>()
            .WithTags("OrderEndpoints");
    }
}
