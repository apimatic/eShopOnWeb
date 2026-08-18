using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(System.Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// GET /api/orders/{orderId}/notifications &ndash; what was sent for this order and what became of each
/// message. Scoped to the caller's own order; each entry carries its own notificationId.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new OrderNotificationsRequest { OrderId = orderId, BuyerId = buyerId }, orderRepository, notificationService);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // A shopper can only see notifications for their own order. Hide the existence of others' orders.
        if (order is null || order.BuyerId != request.BuyerId)
            return Results.NotFound();

        var notifications = await notificationService.GetForOrderAsync(order.Id);
        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
