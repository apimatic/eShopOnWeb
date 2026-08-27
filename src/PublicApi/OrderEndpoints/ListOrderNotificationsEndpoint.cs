using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for an order and what became of each message. Statuses
/// are refreshed from the provider on read (the provider cannot call back into
/// this application). Shoppers see only their own orders; administrators see any.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListOrderNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<List<OrderNotificationDto>>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orders, IRepository<OrderNotification> notifications,
        IOrderNotificationService notificationService)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
    }

    [HttpGet("api/orders/{orderId}/notifications")]
    [SwaggerOperation(Summary = "Lists an order's notifications and their outcomes", Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<List<OrderNotificationDto>>> HandleAsync(
        [FromRoute(Name = "orderId")] int orderId, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return NotFound();

        var isAdmin = User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != buyerId)
        {
            return NotFound();
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _notificationService.RefreshStatusAsync(notification, cancellationToken);
        }

        return notifications.Select(OrderNotificationDto.FromEntity).ToList();
    }
}
