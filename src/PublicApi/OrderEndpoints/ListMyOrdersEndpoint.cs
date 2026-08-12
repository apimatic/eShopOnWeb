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

/// <summary>
/// Lists the calling shopper's orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider as the list is built.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(user, service);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        var orders = await service.ListOrdersForBuyerAsync(buyerId);

        var result = new List<MyOrderDto>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await service.ListNotificationsForOrderAsync(order.Id);
            result.Add(new MyOrderDto
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
                Notifications = notifications.Select(NotificationDto.From).ToList()
            });
        }
        return Results.Ok(result);
    }
}
