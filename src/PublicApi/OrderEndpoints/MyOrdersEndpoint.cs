using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider before being reported.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orders;
    private readonly IReadRepository<Notification> _notifications;
    private readonly INotificationService _notificationService;

    public MyOrdersEndpoint(
        IReadRepository<Order> orders,
        IReadRepository<Notification> notifications,
        INotificationService notificationService)
    {
        _orders = orders;
        _notifications = notifications;
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
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var response = new MyOrdersResponse();
        if (orders.Count == 0)
        {
            return Results.Ok(response);
        }

        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = await _notifications.ListAsync(new NotificationsByOrdersSpecification(orderIds));
        await _notificationService.RefreshStatusesAsync(notifications);

        var byOrder = notifications.ToLookup(n => n.OrderId);
        response.Orders = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = byOrder[o.Id]
                    .OrderByDescending(n => n.CreatedAt)
                    .Select(n => n.ToDto())
                    .ToList()
            })
            .ToList();

        return Results.Ok(response);
    }
}
