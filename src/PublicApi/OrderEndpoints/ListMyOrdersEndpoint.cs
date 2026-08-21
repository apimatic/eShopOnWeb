using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderWorkflowService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRepository<OrderNotification> _notifications;

    public ListMyOrdersEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IRepository<OrderNotification> notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderWorkflowService orders) =>
            {
                return await HandleAsync(orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderWorkflowService orders)
    {
        var buyerId = HttpContextBuyer.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListMyOrdersAsync(buyerId);
        var notifications = myOrders.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new OrderNotificationsByOrderIdsSpecification(myOrders.Select(o => o.Id)));

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse();
        foreach (var order in myOrders)
        {
            var dto = new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total()
            };

            dto.Items.AddRange(order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }));

            if (byOrder.TryGetValue(order.Id, out var orderNotifications))
            {
                dto.Notifications.AddRange(orderNotifications.Select(NotificationDto.From));
            }

            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }
}
