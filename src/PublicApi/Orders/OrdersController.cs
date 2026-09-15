using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
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
using Microsoft.eShopWeb.PublicApi.Notifications;

namespace Microsoft.eShopWeb.PublicApi.Orders;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly NotificationCoordinator _notifications;
    private readonly ITwilioGateway _twilio;

    public OrdersController(CatalogContext db, NotificationCoordinator notifications, ITwilioGateway twilio)
    {
        _db = db;
        _notifications = notifications;
        _twilio = twilio;
    }

    [HttpPost("orders")]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { error = "At least one catalog item is required." });
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return BadRequest(new { error = "Catalog item ids and quantities must be positive." });

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(x => requested.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0) return BadRequest(new { error = "One or more catalog items do not exist.", catalogItemIds = missing });

        var items = catalogItems.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri), x.Price, requested[x.Id])).ToList();
        var address = request.ShippingAddress is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : request.ShippingAddress.ToAddress();
        var order = new Order(BuyerId(), address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.SendToActiveNumbersAsync(order, NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.", null, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        try
        {
            order.Dispatch(DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.SendToActiveNumbersAsync(order, NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} has been dispatched and is on its way.", null, cancellationToken);
        var followUpAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await _notifications.SendToActiveNumbersAsync(order, NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?", followUpAt, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString(), followUpScheduledFor = followUpAt });
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (order.Status == OrderStatus.Cancelled)
            return Ok(new { orderId = order.Id, status = order.Status.ToString() });

        var pendingFollowUps = await _db.OrderNotifications.Where(x => x.OrderId == orderId &&
            x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid != null &&
            x.ProviderDateSent == null && x.ProviderStatus != "canceled").ToListAsync(cancellationToken);
        foreach (var notification in pendingFollowUps)
        {
            try
            {
                var result = await _twilio.CancelMessageAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderResult(result.Sid, result.Status, result.ErrorCode,
                    result.ErrorMessage, result.DateSent);
            }
            catch (TwilioProviderException ex)
            {
                notification.RecordCancellationFailure(ex.ProviderCode, ex.ProviderMessage);
            }
            catch (Exception)
            {
                notification.RecordCancellationFailure(null, "The messaging provider was unavailable during cancellation.");
            }
        }

        order.Cancel(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(CancellationToken.None);
        await _notifications.SendToActiveNumbersAsync(order, NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.", null, cancellationToken);
        return Ok(new { orderId = order.Id, status = order.Status.ToString() });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        var response = orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString(),
            dispatchedAt = order.DispatchedAt,
            cancelledAt = order.CancelledAt,
            total = order.Total(),
            items = order.OrderItems.Select(x => new
            {
                catalogItemId = x.ItemOrdered.CatalogItemId,
                name = x.ItemOrdered.ProductName,
                quantity = x.Units,
                unitPrice = x.UnitPrice
            }),
            notifications = notifications.Where(x => x.OrderId == order.Id).Select(NotificationCoordinator.ToDto)
        });
        return Ok(response);
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IActionResult> OrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken))
            return NotFound();
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId && x.BuyerId == buyerId)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        await _notifications.RefreshAsync(notifications, cancellationToken);
        return Ok(notifications.Select(NotificationCoordinator.ToDto));
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class OrderLineRequest
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

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}
