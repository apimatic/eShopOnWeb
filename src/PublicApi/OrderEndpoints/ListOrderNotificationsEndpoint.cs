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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the signed-in shopper's orders and what became of each
/// message. Non-terminal statuses are refreshed from the provider before responding.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IRepository<Order> orderRepository,
                IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId) { BuyerId = user.Identity!.Name! },
                    orderRepository, notificationRepository, notificationService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListOrderNotificationsRequest request,
        IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        var order = await orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await notificationService.RefreshOrderNotificationStatusesAsync(request.OrderId);

        var notifications = await notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(request.OrderId));

        return Results.Ok(new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
    public string BuyerId { get; set; } = string.Empty;
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
