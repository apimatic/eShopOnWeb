using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's orders and what became of each message. Each entry carries its
/// own notificationId — the identifier the operator endpoints act on. Statuses are refreshed from the provider
/// so the reported outcome is current. A shopper can only see their own order's notifications.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _service;

    public OrderNotificationsEndpoint(
        IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService service)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new OrderNotificationsRequest(orderId), user);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.CallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(request.OrderId));

        // Bring each message's outcome up to date from the provider before reporting it.
        foreach (var notification in notifications)
        {
            await _service.RefreshStatusAsync(notification);
        }

        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
