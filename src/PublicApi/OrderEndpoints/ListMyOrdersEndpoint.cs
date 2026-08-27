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

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<Order> orderRepository,
                IRepository<OrderNotification> notificationRepository,
                ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderRepository, notificationRepository, user);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepository,
        IRepository<OrderNotification> notificationRepository, ClaimsPrincipal user)
    {
        var userName = user.GetUserName();
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(userName));
        var notifications = await notificationRepository.ListAsync(new NotificationsByBuyerSpec(userName));

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new OrderDto
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
                }).ToList(),
                Notifications = notifications.Where(n => n.OrderId == o.Id).Select(ToDto).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }

    internal static NotificationDto ToDto(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        Status = n.Status,
        ErrorMessage = n.ErrorMessage,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed
    };
}
