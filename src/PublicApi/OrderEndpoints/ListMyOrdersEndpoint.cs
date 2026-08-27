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
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationService.GetNotificationsForOrdersAsync(orders.Select(o => o.Id));

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => MapOrder(o, notifications.Where(n => n.OrderId == o.Id))).ToList()
        };

        return Results.Ok(response);
    }

    internal static OrderDto MapOrder(Order order, IEnumerable<OrderNotification> notifications)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = notifications.Select(MapNotification).ToList()
        };
    }

    internal static OrderNotificationDto MapNotification(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            NotificationType = notification.NotificationType.ToString(),
            Status = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            ProviderMessageSid = notification.ProviderMessageSid,
            Body = notification.Body,
            IsContentDisposed = notification.IsContentDisposed,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor
        };
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMyOrdersResponse()
    {
    }

    public List<OrderDto> Orders { get; set; } = new();
}
