using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<SmsNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for one of the caller's own orders and what became of each message. Each entry
/// carries its own notificationId — what the operator endpoints act on. Shopper-scoped: a shopper can
/// only see their own order's notifications.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             ClaimsPrincipal user,
             IReadRepository<Order> orderRepository,
             IRepository<SmsNotification> notificationRepository,
             ISmsNotificationService notificationService) =>
            {
                var buyerId = user.GetUserId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                // Scope to the caller's order; another shopper's order reads as "not found".
                var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdForBuyerSpecification(orderId, buyerId));
                if (order is null)
                    return Results.NotFound();

                var notifications = (await notificationRepository.ListAsync(new SmsNotificationsByOrderSpecification(orderId))).ToList();
                await notificationService.RefreshDeliveryOutcomesAsync(notifications);

                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(SmsNotificationDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}
