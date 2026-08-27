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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for one of the caller's orders and what became of each
/// message, refreshing non-terminal outcomes from the provider.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user,
                IRepository<Order> orderRepository, IRepository<OrderNotification> notificationRepository,
                IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest { OrderId = orderId },
                    user, orderRepository, notificationRepository, notificationService, cancellationToken);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;

        var order = await orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId) throw new OrderNotFoundException(request.OrderId);

        var notifications = (await notificationRepository.ListAsync(cancellationToken))
            .Where(n => n.OrderId == request.OrderId && n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt)
            .ToList();

        foreach (var notification in notifications)
        {
            await notificationService.RefreshFromProviderAsync(notification, cancellationToken);
        }

        var response = new ListOrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(notifications.Select(NotificationDto.FromEntity));
        return Results.Ok(response);
    }
}
