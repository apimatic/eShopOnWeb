using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Shows what was sent for an order and what became of each message. Shopper-scoped: callers
/// see their own orders; administrators (who act on notifications) may see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly NotificationService _notificationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        NotificationService notificationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, ct);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var callerId = user?.Identity?.Name;
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orders.GetByIdAsync(orderId, ct);
        var isAdmin = user!.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (order is null || (!isAdmin && order.BuyerId != callerId))
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), ct);
        await _notificationService.RefreshOutcomesAsync(notifications, ct);

        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(n => NotificationDto.FromEntity(n, includeBody: true)).ToList()
        });
    }
}
