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
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly OrderNotificationService _notifications;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(CatalogContext db, OrderNotificationService notifications,
        ILogger<OrdersController> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpPost("api/orders")]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            return BadRequest(new { errors = new { items = new[] { "At least one item is required." } } });
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return BadRequest(new { errors = new { items = new[] { "Catalog item ids and quantities must be positive." } } });

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            return BadRequest(new { errors = new { items = new[] { "One or more catalog items do not exist." } } });

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(User.Identity!.Name!,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogError("Notification recording failed after order {OrderId} was placed.", order.Id);
        }

        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null) return NotFound();
        try { order.Dispatch(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        await _db.SaveChangesAsync(cancellationToken);

        try { await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken); }
        catch (Exception)
        {
            _logger.LogError("Notification recording failed after order {OrderId} was dispatched.", order.Id);
        }
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null) return NotFound();
        order.Cancel();
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.CancelOutstandingFollowUpsAsync(order, cancellationToken);
            await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogError("Notification handling failed after order {OrderId} was cancelled.", order.Id);
        }
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _db.Orders.Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);

        var result = orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString(),
            total = order.Total(),
            notifications = notifications.Where(x => x.OrderId == order.Id).Select(NotificationDto)
        });
        return Ok(new { orders = result });
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> OrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return NotFound();
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId).OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(new { notifications = notifications.Select(NotificationDto) });
    }

    internal static object NotificationDto(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        type = notification.Kind.ToString(),
        content = notification.Body,
        contentDisposed = notification.IsContentDisposed,
        providerMessageSid = notification.ProviderMessageSid,
        status = notification.ProviderStatus,
        errorCode = notification.ProviderErrorCode,
        error = notification.ProviderErrorMessage,
        createdAt = notification.CreatedAt,
        scheduledFor = notification.ScheduledFor,
        sentAt = notification.ProviderDateSent,
        originalNotificationId = notification.OriginalNotificationId
    };
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)] public List<PlaceOrderItemRequest> Items { get; set; } = new();
    [Required] public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}
