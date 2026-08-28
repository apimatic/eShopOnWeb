using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrderNotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrderNotificationsController(OrderNotificationService service)
    {
        _service = service;
    }

    [HttpPost("contact-numbers")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterContactNumber(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var contact = await _service.RegisterContactNumberAsync(
            BuyerId(),
            request.PhoneNumber,
            request.CountryCode,
            cancellationToken);
        return Created($"/api/contact-numbers/{contact.Id}", new
        {
            contactNumberId = contact.Id,
            phoneNumber = contact.Value
        });
    }

    [HttpGet("contact-numbers")]
    public async Task<IActionResult> GetContactNumbers(CancellationToken cancellationToken)
    {
        var numbers = await _service.GetContactNumbersAsync(BuyerId(), cancellationToken);
        return Ok(numbers.Select(number => new
        {
            contactNumberId = number.Id,
            phoneNumber = number.Value,
            createdAt = number.CreatedAt
        }));
    }

    [HttpDelete("contact-numbers/{contactNumberId:int}")]
    public async Task<IActionResult> RemoveContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        await _service.RemoveContactNumberAsync(BuyerId(), contactNumberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var items = request.Items?.Select(item => new PlaceOrderItem(item.CatalogItemId, item.Quantity)).ToList()
            ?? new List<PlaceOrderItem>();
        if (request.ShippingAddress is null)
        {
            throw new ApiValidationException("shippingAddress is required.");
        }

        var address = new ShippingAddress(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);
        var order = await _service.PlaceOrderAsync(BuyerId(), items, address, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        await _service.DispatchOrderAsync(orderId, cancellationToken);
        return Ok(new { orderId, status = "dispatched" });
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        await _service.CancelOrderAsync(orderId, cancellationToken);
        return Ok(new { orderId, status = "canceled" });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> GetMyOrders(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var orders = await _service.GetBuyerOrdersAsync(buyerId, cancellationToken);
        var notifications = await _service.GetNotificationSummariesAsync(buyerId, cancellationToken);
        return Ok(orders.Select(order => new
        {
            orderId = order.Id,
            orderDate = order.OrderDate,
            status = order.Status.ToString().ToLowerInvariant(),
            total = order.Total(),
            items = order.OrderItems.Select(item => new
            {
                catalogItemId = item.ItemOrdered.CatalogItemId,
                name = item.ItemOrdered.ProductName,
                quantity = item.Units,
                unitPrice = item.UnitPrice
            }),
            notifications = notifications.GetValueOrDefault(order.Id, new List<OrderNotification>())
                .Select(NotificationSummary)
        }));
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    public async Task<IActionResult> GetOrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _service.GetOrderNotificationsAsync(orderId, BuyerId(), cancellationToken);
        return Ok(notifications.Select(NotificationDetail));
    }

    [HttpPost("notifications/{notificationId:int}/resend")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> ResendNotification(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var resend = await _service.ResendNotificationAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created($"/api/orders/{resend.OrderId}/notifications", new { notificationId = resend.Id });
    }

    [HttpDelete("notifications/{notificationId:int}/content")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DisposeNotificationContent(int notificationId, CancellationToken cancellationToken)
    {
        await _service.DisposeNotificationContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("notifications/reconciliation")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconcile(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var entries = await _service.ReconcileAsync(from, to, cancellationToken);
        return Ok(new { from, to, entries });
    }

    private string BuyerId() =>
        User.Identity?.Name ?? throw new UnauthorizedAccessException("The token does not identify a shopper.");

    private static object NotificationSummary(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        type = notification.Type.ToString(),
        status = notification.ProviderStatus,
        providerMessageSid = notification.ProviderMessageSid,
        errorCode = notification.ProviderErrorCode,
        scheduledFor = notification.ScheduledFor
    };

    private static object NotificationDetail(OrderNotification notification) => new
    {
        notificationId = notification.Id,
        type = notification.Type.ToString(),
        content = notification.Body,
        contentRedacted = notification.IsContentRedacted,
        providerMessageSid = notification.ProviderMessageSid,
        status = notification.ProviderStatus,
        errorCode = notification.ProviderErrorCode,
        createdAt = notification.CreatedAt,
        scheduledFor = notification.ScheduledFor,
        providerDateCreated = notification.ProviderDateCreated,
        providerDateSent = notification.ProviderDateSent,
        lastSyncedAt = notification.LastSyncedAt,
        parentNotificationId = notification.ParentNotificationId
    };
}

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
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
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
