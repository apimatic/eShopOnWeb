using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's own orders and what became of each message. Each
/// entry carries its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(new OrderNotificationsRequest { OrderId = orderId, BuyerId = buyerId }, orderRepository, notificationService);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // Shopper-scoped: another shopper's order is treated as not found.
        if (order is null || order.BuyerId != request.BuyerId)
            return Results.NotFound();

        var notifications = await notificationService.GetOrderNotificationsAsync(request.OrderId, refresh: true);

        var response = new OrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.FromEntity));
        return Results.Ok(response);
    }
}

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal string? BuyerId { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
