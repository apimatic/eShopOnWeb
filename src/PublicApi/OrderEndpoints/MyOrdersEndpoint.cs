using System.Collections.Generic;
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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderView> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to (delivery outcome
/// refreshed from the provider).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            // ClaimsPrincipal param keeps this off the RequestDelegate overload (identity is read from HttpContext.User).
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, System.Security.Claims.ClaimsPrincipal user) => await HandleAsync(http))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var ct = http.RequestAborted;
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationRepository = http.RequestServices.GetRequiredService<IRepository<Notification>>();
        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var orders = await orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId), ct);

        // Notifications are scoped to this shopper; refresh their provider-owned state, then group by order.
        var notifications = await notificationRepository.ListAsync(new NotificationsByOwnerSpecification(buyerId), ct);
        await notifier.RefreshStatusesAsync(notifications, ct);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderView
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                Total = o.Total(),
                OrderDate = o.OrderDate,
                Notifications = byOrder.TryGetValue(o.Id, out var list)
                    ? list.Select(NotificationView.From).ToList()
                    : new List<NotificationView>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
