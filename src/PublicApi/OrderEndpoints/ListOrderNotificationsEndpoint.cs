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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the signed-in shopper's orders, and what became of each message.
/// Delivery outcomes are refreshed from the provider on a best-effort basis.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(orderId, user, http);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user, HttpContext http)
    {
        // Stateful services come from the request scope: endpoint instances are built once at
        // startup, so anything constructor-injected would hold a stale DbContext for the
        // process lifetime.
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationRepository = http.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var buyerId = user.GetBuyerId();
        var order = await orderRepository.GetByIdAsync(orderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));
        foreach (var notification in notifications)
        {
            await notificationService.RefreshStatusAsync(notification);
        }

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        });
    }
}
