using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's own orders and what became of each message. Each
/// entry carries its <c>notificationId</c> — the handle the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IReadRepository<Order>>
{
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderNotificationsEndpoint(IOrderNotificationService notificationService, IHttpContextAccessor httpContextAccessor)
    {
        _notificationService = notificationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository) =>
                await HandleAsync(new GetOrderNotificationsRequest(orderId), orderRepository))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IReadRepository<Order> orderRepository)
    {
        var ownerId = _httpContextAccessor.GetCallerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        // A shopper must never see another's order: an order that isn't the caller's is simply not found.
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != ownerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetOrderNotificationsAsync(request.OrderId);

        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = (notifications ?? Array.Empty<OrderNotification>())
                .Select(NotificationDto.FromEntity)
                .ToList()
        };

        return Results.Ok(response);
    }
}
