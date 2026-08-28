using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly OrderNotificationService _notifications;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _clock;

    public OrdersController(CatalogContext db, OrderNotificationService notifications,
        IUriComposer uriComposer, TimeProvider clock)
    {
        _db = db;
        _notifications = notifications;
        _uriComposer = uriComposer;
        _clock = clock;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 ||
            request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 100))
        {
            return BadRequest(new { error = "At least one catalog item with a quantity from 1 to 100 is required." });
        }

        var requestedItems = request.Items.GroupBy(x => x.CatalogItemId)
            .Select(x => new PlaceOrderItemRequest
            {
                CatalogItemId = x.Key,
                Quantity = x.Sum(y => y.Quantity)
            }).ToList();
        if (requestedItems.Any(x => x.Quantity > 100))
        {
            return BadRequest(new { error = "The combined quantity for a catalog item cannot exceed 100." });
        }

        var ids = requestedItems.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            return BadRequest(new { error = "One or more catalog items do not exist." });
        }

        var items = requestedItems.Select(requested =>
        {
            var catalog = catalogItems.Single(x => x.Id == requested.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalog.Id, catalog.Name, _uriComposer.ComposePicUri(catalog.PictureUri)),
                catalog.Price, requested.Quantity);
        }).ToList();
        var address = request.ShippingAddress?.ToDomain() ??
                      new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided");
        var order = new Order(User.Identity!.Name!, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        try
        {
            if (order.Dispatch(_clock.GetUtcNow()))
            {
                await _db.SaveChangesAsync(cancellationToken);
                await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
            }
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Cancel(_clock.GetUtcNow()))
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _notifications.CancelPendingFollowUpsForOrderAsync(order.Id, cancellationToken);
            await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId)).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await _notifications.RefreshProviderStatesAsync(notifications, cancellationToken);

        var response = orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString(),
            total = order.Total(),
            notifications = notifications.Where(x => x.OrderId == order.Id).Select(ToSummary)
        });
        return Ok(new { orders = response });
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IActionResult> OrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId,
            cancellationToken);
        if (!ownsOrder)
        {
            return NotFound();
        }

        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await _notifications.RefreshProviderStatesAsync(notifications, cancellationToken);
        return Ok(new
        {
            orderId,
            notifications = notifications.Select(x => new
            {
                notificationId = x.Id,
                type = x.Kind.ToString(),
                content = x.Body,
                contentRedacted = x.ContentRedacted,
                providerMessageId = x.ProviderMessageSid,
                status = x.ProviderStatus,
                providerErrorCode = x.ProviderErrorCode,
                createdAt = x.CreatedAt,
                scheduledFor = x.ScheduledFor,
                sentAt = x.ProviderSentAt,
                resendOfNotificationId = x.ResendOfNotificationId
            })
        });
    }

    private static object ToSummary(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        type = notification.Kind.ToString(),
        status = notification.ProviderStatus,
        providerErrorCode = notification.ProviderErrorCode,
        scheduledFor = notification.ScheduledFor
    };
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToDomain() => new(Required(Street), Required(City), State ?? string.Empty,
        Required(Country), Required(ZipCode));

    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;
}
