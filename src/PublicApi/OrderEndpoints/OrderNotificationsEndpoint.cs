using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/orders/{orderId}/notifications — what was sent for this order and what became of each message.
/// Shopper-scoped: the caller must own the order. Each entry carries its own notificationId (what the operator
/// endpoints act on). Delivery outcomes are refreshed from the provider on read.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderIdRequest, IRepository<Order>>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderNotificationsEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notifications,
        IHttpContextAccessor httpContextAccessor)
    {
        _notificationRepository = notificationRepository;
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepository) =>
                await HandleAsync(new OrderIdRequest(orderId), orderRepository))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IRepository<Order> orderRepository)
    {
        var buyerId = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        // Ownership check doubles as the not-found response, so a shopper can never probe another's order.
        var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpecification(request.OrderId, buyerId));
        if (order is null)
            return Results.NotFound();

        var notifications = (await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id))).ToList();
        await _notifications.RefreshDeliveryStateAsync(notifications, CancellationToken.None);

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
