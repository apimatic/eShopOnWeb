using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.Orders;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly OrderNotificationDispatcher _notifications;

    public OrdersController(CatalogContext db, OrderNotificationDispatcher notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> Place(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
        {
            ModelState.AddModelError(nameof(request.Items), "At least one item with a positive quantity is required.");
            return ValidationProblem(ModelState);
        }

        var combinedItems = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogIds = combinedItems.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => catalogIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missingIds = catalogIds.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
        {
            ModelState.AddModelError(nameof(request.Items), $"Catalog item ids were not found: {string.Join(", ", missingIds)}.");
            return ValidationProblem(ModelState);
        }

        var address = request.ShipToAddress;
        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, combinedItems[item.Id])).ToList();
        var order = new Order(UserId(), new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await BestEffortNotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderPlaced,
            $"eShopOnWeb: Order {order.Id} has been placed.", null);

        return Created($"/api/my-orders", new CreateOrderResponse(order.Id));
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Conflict(new ProblemDetails { Detail = "A cancelled order cannot be dispatched." });
        }

        var dispatchedAt = DateTimeOffset.UtcNow;
        if (!order.Dispatch(dispatchedAt))
        {
            return Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await BestEffortNotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched,
            $"eShopOnWeb: Order {order.Id} has been dispatched and is on its way.", null);
        await BestEffortNotifyAsync(order.Id, order.BuyerId, NotificationKind.DeliveryFollowUp,
            $"eShopOnWeb: How did delivery of order {order.Id} go?", dispatchedAt + OrderNotificationDispatcher.FollowUpDelay);

        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var changed = order.Cancel(DateTimeOffset.UtcNow);
        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await _notifications.CancelScheduledForOrderAsync(order.Id, CancellationToken.None);
        }
        catch
        {
            // Order cancellation is durable and a repeated request retries provider cancellation.
        }

        if (changed)
        {
            await BestEffortNotifyAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled,
                $"eShopOnWeb: Order {order.Id} has been cancelled.", null);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<MyOrderResponse[]>> MyOrders(CancellationToken cancellationToken)
    {
        var ownerId = UserId();
        var orders = await _db.Orders.Include(x => x.OrderItems)
            .Where(x => x.BuyerId == ownerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);

        return Ok(orders.Select(order => new MyOrderResponse(order.Id, order.OrderDate,
            order.Status.ToString(), order.Total(), notifications.Where(x => x.OrderId == order.Id)
                .Select(ToNotificationResponse).ToArray())).ToArray());
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<ActionResult<NotificationResponse[]>> Notifications(int orderId,
        CancellationToken cancellationToken)
    {
        var ownerId = UserId();
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == ownerId,
            cancellationToken);
        if (!ownsOrder)
        {
            return NotFound();
        }

        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(notifications.Select(ToNotificationResponse).ToArray());
    }

    private async Task BestEffortNotifyAsync(int orderId, string ownerId, NotificationKind kind,
        string body, DateTimeOffset? scheduledFor)
    {
        try
        {
            await _notifications.NotifyActiveContactsAsync(orderId, ownerId, kind, body, scheduledFor,
                CancellationToken.None);
        }
        catch
        {
            // Notification persistence/provider failures never roll back the order operation.
        }
    }

    internal static NotificationResponse ToNotificationResponse(OrderNotification notification) => new(
        notification.Id, notification.Kind.ToString(), notification.Body, notification.ContentRedacted,
        notification.ProviderMessageSid, notification.ProviderStatus, notification.ProviderErrorCode,
        notification.ProviderErrorMessage, notification.CreatedAt, notification.ProviderDateSent,
        notification.ScheduledFor, notification.LastCheckedAt, notification.OriginalNotificationId);

    private string UserId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    [Required]
    public ShippingAddressRequest ShipToAddress { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }
    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, StringLength(180)] public string Street { get; set; } = string.Empty;
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [StringLength(60)] public string State { get; set; } = string.Empty;
    [Required, StringLength(90)] public string Country { get; set; } = string.Empty;
    [Required, StringLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    NotificationResponse[] Notifications);
public sealed record NotificationResponse(int NotificationId, string Kind, string? Content, bool ContentRedacted,
    string? ProviderMessageId, string ProviderStatus, int? ProviderErrorCode, string? ProviderErrorMessage,
    DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? ScheduledFor,
    DateTimeOffset? LastCheckedAt, int? OriginalNotificationId);
