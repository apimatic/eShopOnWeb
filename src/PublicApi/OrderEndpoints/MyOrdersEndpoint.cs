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
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        IOrderNotificationService notificationService,
        IHttpContextAccessor http)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            () => await HandleAsync())
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var ct = _http.HttpContext!.RequestAborted;
        var ownerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), ct);
        var orderIds = orders.Select(o => o.Id).ToList();

        var notifications = orderIds.Count == 0
            ? new List<OrderNotification>()
            : (await _notifications.ListAsync(new OrderNotificationsByOwnerSpecification(ownerId, orderIds), ct)).ToList();

        // Bring the delivery outcomes up to date from the provider (best-effort).
        await _notificationService.RefreshStatusesAsync(notifications, ct);

        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = byOrder.TryGetValue(o.Id, out var list)
                    ? list.Select(NotificationDto.From).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
