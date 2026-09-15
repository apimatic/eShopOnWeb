using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private const string Administrators = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly CatalogContext _context;
    private readonly OrderNotificationCoordinator _notifications;

    public OrdersController(CatalogContext context, OrderNotificationCoordinator notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    [HttpPost("api/orders")]
    public async Task<IActionResult> Place([FromBody] PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0) return BadRequest(new { error = "At least one item is required." });
        if (request.Items.Any(x => x.Quantity <= 0)) return BadRequest(new { error = "Every quantity must be greater than zero." });

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _context.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0) return BadRequest(new { error = "One or more catalog items do not exist.", catalogItemIds = missing });

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = new Address("Not supplied through PublicApi", "Not supplied", string.Empty, "Not supplied", "N/A");
        var order = new Order(User.Identity!.Name!, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await _notifications.SendForOrderAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (order.Status == OrderStatus.Cancelled) return Conflict(new { error = "A cancelled order cannot be dispatched." });

        if (order.Dispatch(DateTimeOffset.UtcNow))
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _notifications.SendForOrderAsync(order, NotificationKind.OrderDispatched,
                $"Your eShop order #{order.Id} is on its way.", null, cancellationToken);
            var followUpAt = DateTimeOffset.UtcNow.AddDays(3);
            await _notifications.SendForOrderAsync(order, NotificationKind.DeliveryFollowUp,
                $"How did delivery of eShop order #{order.Id} go?", followUpAt, cancellationToken);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString(), dispatchedAt = order.DispatchedAt });
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = Administrators, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();

        if (order.Cancel(DateTimeOffset.UtcNow))
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _notifications.CancelScheduledForOrderAsync(order.Id, null, cancellationToken);
            await _notifications.SendForOrderAsync(order, NotificationKind.OrderCancelled,
                $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString(), cancelledAt = order.CancelledAt });
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _context.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notificationEntities = await _context.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notificationEntities, cancellationToken);

        var response = orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString(),
            total = order.Total(),
            notifications = notificationEntities.Where(x => x.OrderId == order.Id).OrderBy(x => x.Id)
                .Select(NotificationDto.From)
        });
        return Ok(new { orders = response });
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        if (!await _context.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return NotFound();
        var notifications = await _context.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(new { orderId, notifications = notifications.Select(NotificationDto.From) });
    }
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, int.MaxValue)] public int Quantity { get; set; }
}

public sealed record NotificationDto(int NotificationId, string Kind, string Status, int? ProviderErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ProviderDateSent,
    bool ContentDisposed, int? OriginalNotificationId)
{
    public static NotificationDto From(OrderNotification notification) => new(notification.Id,
        notification.Kind.ToString(), notification.ProviderStatus, notification.ProviderErrorCode,
        notification.CreatedAt, notification.ScheduledFor, notification.ProviderDateSent,
        notification.ContentDisposedAt.HasValue, notification.OriginalNotificationId);
}
