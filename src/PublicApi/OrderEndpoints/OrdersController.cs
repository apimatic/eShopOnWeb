using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class OrdersController : ControllerBase
{
    private readonly CatalogContext _context;
    private readonly OrderNotificationService _notifications;

    public OrdersController(CatalogContext context, OrderNotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    [HttpPost]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { error = "At least one catalog item is required." });
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return BadRequest(new { error = "Catalog item ids and quantities must be positive." });
        if (request.ShippingAddress is null || !request.ShippingAddress.IsComplete())
            return BadRequest(new { error = "A complete shippingAddress is required." });

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity));
        var catalogItems = await _context.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).OrderBy(x => x).ToArray();
        if (missing.Length > 0)
            return BadRequest(new { error = "One or more catalog items do not exist.", catalogItemIds = missing });

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(User.Identity!.Name!,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.NotifyCurrentContactsAsync(order, OrderNotificationService.Placed,
                $"eShopOnWeb order {order.Id} has been placed.", cancellationToken: cancellationToken);
        }
        catch
        {
            // Notification persistence or delivery must not roll back a successfully placed order.
        }

        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (!order.Dispatch(DateTimeOffset.UtcNow))
            return Conflict(new { error = $"An order in status {order.Status} cannot be dispatched." });
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.NotifyCurrentContactsAsync(order, OrderNotificationService.Dispatched,
                $"eShopOnWeb order {order.Id} has been dispatched and is on its way.",
                cancellationToken: cancellationToken);
            await _notifications.NotifyCurrentContactsAsync(order, OrderNotificationService.DeliveryFollowUp,
                $"How did delivery of eShopOnWeb order {order.Id} go?",
                DateTimeOffset.UtcNow.AddDays(3), cancellationToken);
        }
        catch
        {
            // Dispatch remains successful even when notification work cannot complete.
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString().ToLowerInvariant() });
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (!order.Cancel(DateTimeOffset.UtcNow))
            return Conflict(new { error = "The order is already cancelled." });
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifications.CancelOutstandingFollowUpsAsync(order.Id,
                cancellationToken: cancellationToken);
            await _notifications.NotifyCurrentContactsAsync(order, OrderNotificationService.Cancelled,
                $"eShopOnWeb order {order.Id} has been cancelled.", cancellationToken: cancellationToken);
        }
        catch
        {
            // The durable cancellation intent is retried in the background; order cancellation succeeds.
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString().ToLowerInvariant() });
    }

    [HttpGet("{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var ownsOrder = await _context.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId,
            cancellationToken);
        if (!ownsOrder) return NotFound();
        var notifications = await _notifications.GetAndRefreshForOrderAsync(orderId, cancellationToken);
        return Ok(notifications.Select(NotificationDto.From));
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

    public bool IsComplete() => !string.IsNullOrWhiteSpace(Street)
        && !string.IsNullOrWhiteSpace(City) && !string.IsNullOrWhiteSpace(Country)
        && !string.IsNullOrWhiteSpace(ZipCode);
}
