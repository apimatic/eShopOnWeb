using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.OrderNotifications;

namespace Microsoft.eShopWeb.PublicApi.Orders;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly NotificationCoordinator _notifications;

    public OrdersController(CatalogContext db, NotificationCoordinator notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            return BadRequest(new { error = "At least one catalog item with a positive quantity is required." });
        }

        if (request.ShippingAddress == null || !request.ShippingAddress.IsValid())
        {
            return BadRequest(new { error = "A complete shippingAddress is required." });
        }

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(i => i.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
        {
            return BadRequest(new { error = "One or more catalog items do not exist." });
        }

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(
            User.Identity!.Name!,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("{orderId:int}/dispatch")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status != OrderStatus.Placed)
        {
            return Conflict(new { error = "Only a placed order can be dispatched." });
        }

        order.Dispatch(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            await _notifications.CancelPendingFollowUpsForOrderAsync(order.Id, cancellationToken);
            return Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }

        order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifications.CancelPendingFollowUpsForOrderAsync(order.Id, cancellationToken);
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
        {
            return NotFound();
        }

        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(new { notifications = notifications.Select(NotificationDto.FromEntity) });
    }
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

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Street) &&
        !string.IsNullOrWhiteSpace(City) &&
        !string.IsNullOrWhiteSpace(Country) &&
        !string.IsNullOrWhiteSpace(ZipCode);
}
