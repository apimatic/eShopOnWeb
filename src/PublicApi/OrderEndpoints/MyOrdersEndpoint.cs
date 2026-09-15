using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.GetBuyerId() }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var orders = await service.GetOrdersAsync(request.BuyerId);
        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = await service.GetNotificationsForOrdersAsync(request.BuyerId, orderIds);
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(oi => new OrderItemDto
                {
                    CatalogItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    UnitPrice = oi.UnitPrice,
                    Units = oi.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var list)
                    ? NotificationDto.From(list).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
