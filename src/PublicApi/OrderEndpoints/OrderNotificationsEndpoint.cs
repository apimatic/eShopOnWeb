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
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of
/// each message. Owner-scoped: the caller must own the order. Each entry carries its
/// notificationId, which the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                ClaimsPrincipal user,
                IReadRepository<OrderStatusRecord> statusRepository,
                IRepository<OrderNotification> notificationRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                var statusRecord = await statusRepository.FirstOrDefaultAsync(
                    new OrderStatusRecordByOrderIdSpecification(orderId), cancellationToken);

                // Unknown order, or one owned by another shopper, is indistinguishable from absent.
                if (statusRecord is null || statusRecord.BuyerId != buyerId)
                    return Results.NotFound();

                var notifications = await notificationRepository.ListAsync(
                    new OrderNotificationsByOrderIdSpecification(orderId), cancellationToken);
                await notificationService.RefreshStatusesAsync(notifications, cancellationToken);

                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(NotificationDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
