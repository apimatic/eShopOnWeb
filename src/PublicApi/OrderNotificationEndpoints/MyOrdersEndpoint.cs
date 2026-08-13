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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IOrderMessagingService service,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetOwnerId(user);
                var orders = await service.GetMyOrdersAsync(buyerId, cancellationToken);
                var orderIds = orders.Select(o => o.Id).ToList();

                var notifications = await service.GetNotificationsForOrdersAsync(orderIds, cancellationToken);
                var byOrder = notifications
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new OrderWithNotificationsDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Status = o.Status.ToString(),
                        Total = o.Total(),
                        Items = OrderDto.From(o).Items,
                        Notifications = byOrder.TryGetValue(o.Id, out var list) ? list : new List<NotificationDto>()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
