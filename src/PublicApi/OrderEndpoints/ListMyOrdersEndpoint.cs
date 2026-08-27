using System.Linq;
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
/// Lists the signed-in shopper's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IRepository<Order>, IRepository<OrderNotification>>
{
    private readonly IOrderNotificationService _notificationService;

    public ListMyOrdersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IRepository<Order> orderRepository, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(httpContext, orderRepository, notificationRepository);
            })
            .Produces<OrderDto[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IRepository<Order> orderRepository, IRepository<OrderNotification> notificationRepository)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var ordersSpec = new CustomerOrdersWithItemsSpecification(buyerId);
        var orders = await orderRepository.ListAsync(ordersSpec, httpContext.RequestAborted);

        var result = orders.Select(o => new OrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            Items = o.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        }).ToList();

        foreach (var dto in result)
        {
            var notificationsSpec = new NotificationsByOrderSpecification(dto.OrderId);
            var notifications = await notificationRepository.ListAsync(notificationsSpec, httpContext.RequestAborted);
            await _notificationService.RefreshStatusesAsync(notifications, httpContext.RequestAborted);
            dto.Notifications = notifications.Select(NotificationDtoMapper.ToDto).ToList();
        }

        return Results.Ok(result);
    }
}
