using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for an order, and what became of each message. Visible to the order's
/// owner and to operators (who act on the returned notificationIds).
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                var request = new ListOrderNotificationsRequest(orderId)
                {
                    BuyerId = httpContext.User.Identity?.Name,
                    IsOperator = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)
                };
                var services = httpContext.RequestServices;
                return await HandleAsync(request,
                    services.GetRequiredService<IRepository<Order>>(),
                    services.GetRequiredService<IRepository<OrderNotification>>(),
                    services.GetRequiredService<IOrderNotificationService>());
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        var response = new ListOrderNotificationsResponse(request.CorrelationId());

        var order = await orderRepository.GetByIdAsync(request.OrderId);

        // A shopper must never see another shopper's order.
        if (order == null || (!request.IsOperator && order.BuyerId != request.BuyerId))
        {
            return Results.NotFound(response);
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(request.OrderId));
        await notificationService.RefreshStatusesAsync(notifications);

        response.OrderId = order.Id;
        response.Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList();
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
    public string? BuyerId { get; set; }
    public bool IsOperator { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
