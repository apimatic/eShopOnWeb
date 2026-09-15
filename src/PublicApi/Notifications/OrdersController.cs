using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly IOrderNotificationService _notifications;

    public OrdersController(CatalogContext db, IOrderNotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpPost("api/orders")]
    [ProducesResponseType<OrderCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceAsync(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requestedItems.Count == 0 || requestedItems.Any(x => x.Quantity is < 1 or > 1000))
            return ValidationProblem(title: "At least one valid catalog item is required.");

        var ids = requestedItems.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var missingIds = ids.Where(x => !catalogItems.ContainsKey(x)).ToList();
        if (missingIds.Count > 0)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["items"] = new[] { $"Unknown catalog item ids: {string.Join(", ", missingIds)}." }
            }));

        var orderItems = requestedItems.Select(x =>
        {
            var item = catalogItems[x.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price, x.Quantity);
        }).ToList();
        var address = ToAddress(request.ShippingAddress);
        var order = new Order(User.Identity!.Name!, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.SendOrderEventAsync(order, NotificationKind.OrderPlaced, cancellationToken);
        return Created($"/api/orders/{order.Id}", new OrderCreatedResponse(order.Id));
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderStateResponse>> DispatchAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (!order.Dispatch())
            return Conflict(new ProblemDetails { Title = $"An order in state {order.Status} cannot be dispatched." });

        await _db.SaveChangesAsync(cancellationToken);
        await _notifications.SendDispatchNotificationsAsync(order, cancellationToken);
        return Ok(new OrderStateResponse(order.Id, order.Status.ToString()));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderStateResponse>> CancelAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return NotFound();
        if (order.Status == OrderStatus.Cancelled)
            return Ok(new OrderStateResponse(order.Id, order.Status.ToString()));

        order.Cancel();
        await _db.SaveChangesAsync(cancellationToken);
        await _notifications.CancelScheduledAsync(order.Id, null, cancellationToken);
        await _notifications.SendOrderEventAsync(order, NotificationKind.OrderCancelled, cancellationToken);
        return Ok(new OrderStateResponse(order.Id, order.Status.ToString()));
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<MyOrderDto>>> MyOrdersAsync(CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var response = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var notifications = await _notifications.GetForOrderAsync(order.Id, cancellationToken);
            response.Add(new MyOrderDto(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
                notifications.Select(ToSummary).ToList()));
        }
        return Ok(response);
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> NotificationsAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var ownsOrder = await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId,
            cancellationToken);
        if (!ownsOrder) return NotFound();
        var notifications = await _notifications.GetForOrderAsync(orderId, cancellationToken);
        return Ok(notifications.Select(ToDto).ToList());
    }

    private static Address ToAddress(ShippingAddressRequest? value) => new(
        string.IsNullOrWhiteSpace(value?.Street) ? "Not supplied through PublicApi" : value.Street,
        string.IsNullOrWhiteSpace(value?.City) ? "Not supplied" : value.City,
        value?.State ?? string.Empty,
        string.IsNullOrWhiteSpace(value?.Country) ? "Not supplied" : value.Country,
        string.IsNullOrWhiteSpace(value?.ZipCode) ? "Not supplied" : value.ZipCode);

    internal static NotificationSummaryDto ToSummary(OrderNotification value) => new(value.Id,
        value.Kind.ToString(), value.ProviderStatus, value.ProviderMessageSid, value.ScheduledFor,
        value.ProviderDateSent, value.ContentDisposedAt);

    internal static NotificationDto ToDto(OrderNotification value) => new(value.Id, value.OrderId,
        value.Kind.ToString(), value.ProviderStatus, value.Body, value.ProviderMessageSid,
        value.ProviderErrorCode, value.CreatedAt, value.ScheduledFor, value.ProviderDateSent,
        value.ContentDisposedAt, value.OriginalNotificationId);
}
