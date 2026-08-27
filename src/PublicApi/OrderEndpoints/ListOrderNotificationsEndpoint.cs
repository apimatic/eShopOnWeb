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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for an order and what became of each message. Shopper-scoped to
/// the caller's own orders; administrators may view any order. Delivery outcomes are
/// refreshed from the provider (there is no callback URL, so we ask the provider).
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        var orderRepository = httpContext.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var buyerId = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.GetByIdAsync(orderId);
        var isAdmin = httpContext.User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (order is null || (order.BuyerId != buyerId && !isAdmin))
        {
            return Results.NotFound();
        }

        var notifications = await notificationRepository.ListAsync(new NotificationsByOrderSpecification(orderId));

        foreach (var notification in notifications)
        {
            await notificationService.RefreshProviderStatusAsync(notification, httpContext.RequestAborted);
        }

        var response = new ListOrderNotificationsResponse { OrderId = orderId };
        response.Notifications.AddRange(notifications.Select(NotificationMapper.ToDto));
        return Results.Ok(response);
    }
}
