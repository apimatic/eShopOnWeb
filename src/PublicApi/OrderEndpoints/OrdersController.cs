using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class OrdersController : ControllerBase
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly CatalogContext _context;
    private readonly NotificationCoordinator _notifications;
    private readonly TimeProvider _clock;

    public OrdersController(CatalogContext context, NotificationCoordinator notifications, TimeProvider clock)
    {
        _context = context;
        _notifications = notifications;
        _clock = clock;
    }

    [HttpPost("api/orders")]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity));
        if (requestedItems.Count == 0 || requestedItems.Values.Any(quantity => quantity <= 0 || quantity > 1000))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Items)] = new[] { "Supply at least one catalog item with a quantity from 1 to 1000." }
            }));
        }

        var catalogItems = await _context.CatalogItems
            .Where(x => requestedItems.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requestedItems.Count)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.Items)] = new[] { "One or more catalog item identifiers do not exist." }
            }));
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requestedItems[item.Id])).ToList();
        var address = new Address(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State ?? string.Empty,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);
        var order = new Order(User.Identity!.Name!, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status == OrderState.Cancelled)
        {
            return Conflict(new { message = "A cancelled order cannot be dispatched." });
        }

        if (order.Dispatch(_clock.GetUtcNow()))
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString().ToLowerInvariant() });
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Cancel(_clock.GetUtcNow()))
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);
        }

        return Ok(new { orderId = order.Id, status = order.Status.ToString().ToLowerInvariant() });
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(x => x.OrderItems)
            .Where(x => x.BuyerId == ownerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _context.OrderNotifications
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);

        var byOrder = notifications.GroupBy(x => x.OrderId).ToDictionary(x => x.Key, x => x.ToList());
        return Ok(orders.Select(order => new
        {
            orderId = order.Id,
            placedAt = order.OrderDate,
            status = order.Status.ToString().ToLowerInvariant(),
            total = order.Total(),
            notifications = byOrder.GetValueOrDefault(order.Id, new()).Select(NotificationDto.FromEntity)
        }));
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> OrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var ownerId = User.Identity!.Name!;
        if (!await _context.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == ownerId, cancellationToken))
        {
            return NotFound();
        }

        var notifications = await _context.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(notifications.Select(NotificationDto.FromEntity));
    }
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; init; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    [Required, StringLength(180)] public string Street { get; init; } = string.Empty;
    [Required, StringLength(100)] public string City { get; init; } = string.Empty;
    [StringLength(60)] public string? State { get; init; }
    [Required, StringLength(90)] public string Country { get; init; } = string.Empty;
    [Required, StringLength(18)] public string ZipCode { get; init; } = string.Empty;
}
