using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
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
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(user.Identity!.Name!));

        var response = new ListMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(order.Id));
            foreach (var notification in notifications)
            {
                await _notificationService.RefreshStatusAsync(notification);
            }

            response.Orders.Add(new OrderDto
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
                Notifications = notifications.Select(n => new NotificationSummaryDto
                {
                    NotificationId = n.Id,
                    Kind = n.Kind.ToString(),
                    Status = n.Status,
                    ErrorCode = n.ErrorCode
                }).ToList()
            });
        }

        return Results.Ok(response);
    }
}
