using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IOrderNotificationService orderNotificationService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderNotificationService.GetOrdersForBuyerAsync(buyerId, cancellationToken);
                var notifications = await orderNotificationService.GetNotificationsForBuyerAsync(buyerId, cancellationToken);
                var notificationsByOrder = notifications
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(order => new OrderSummaryDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Items = order.OrderItems.Select(i => new OrderItemDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Units = i.Units
                        }).ToList(),
                        Notifications = notificationsByOrder.TryGetValue(order.Id, out var list)
                            ? list.Select(NotificationMapping.ToStatusDto).ToList()
                            : new List<NotificationStatusDto>()
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
