using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/contact-numbers")]
public sealed class ContactNumbersController : ControllerBase
{
    private readonly OrderNotificationService _service;
    public ContactNumbersController(OrderNotificationService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RegisterContactAsync(Identity(), request.Number, request.CountryCode, cancellationToken);
            if (!result.Valid) return BadRequest(new { errors = result.Errors });
            var body = new { contactNumberId = result.Contact!.Id, number = result.Contact.E164Number };
            return result.Created ? Created($"/api/contact-numbers/{result.Contact.Id}", body) : Ok(body);
        }
        catch (TwilioProviderException) { return StatusCode(StatusCodes.Status502BadGateway, new { error = "Phone-number validation is temporarily unavailable." }); }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var contacts = await _service.GetContactsAsync(Identity(), cancellationToken);
        return Ok(new { contactNumbers = contacts.Select(x => new { contactNumberId = x.Id, number = x.E164Number, createdAt = x.CreatedAt }) });
    }

    [HttpDelete("{contactNumberId:int}")]
    public async Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken) =>
        await _service.DeleteContactAsync(Identity(), contactNumberId, cancellationToken) ? NoContent() : NotFound();

    private string Identity() => User.Identity?.Name ?? throw new UnauthorizedAccessException();
}

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderNotificationService _service;
    public OrdersController(OrderNotificationService service) => _service = service;

    [HttpPost("api/orders")]
    public async Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Items is null || request.ShippingAddress is null) return BadRequest(new { error = "Items and shippingAddress are required." });
            var address = request.ShippingAddress;
            if (new[] { address.Street, address.City, address.Country, address.ZipCode }.Any(string.IsNullOrWhiteSpace))
                return BadRequest(new { error = "A complete shipping address is required." });
            var order = await _service.PlaceOrderAsync(Identity(), request.Items.Select(x =>
                new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                new Address(address.Street, address.City, address.State ?? string.Empty, address.Country, address.ZipCode), cancellationToken);
            return Created($"/api/orders/{order.Id}", new { orderId = order.Id, status = order.Status.ToString(), orderDate = order.OrderDate });
        }
        catch (RequestValidationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _service.GetOrdersAsync(Identity(), cancellationToken);
        return Ok(new { orders = orders.Select(x => new
        {
            orderId = x.Order.Id,
            status = x.Order.Status.ToString(),
            orderDate = x.Order.OrderDate,
            dispatchedAt = x.Order.DispatchedAt,
            cancelledAt = x.Order.CancelledAt,
            total = x.Order.Total(),
            notifications = x.Notifications.Select(NotificationResponse.From)
        }) });
    }

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await _service.GetOrderNotificationsAsync(Identity(), orderId, cancellationToken);
            return Ok(new { orderId, notifications = notifications.Select(NotificationResponse.From) });
        }
        catch (ResourceNotFoundException) { return NotFound(); }
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.DispatchAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.CancelAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (TwilioProviderException) { return StatusCode(StatusCodes.Status502BadGateway, new { error = "The order was cancelled, but provider follow-up cancellation could not be confirmed. Retry this request." }); }
    }

    private string Identity() => User.Identity?.Name ?? throw new UnauthorizedAccessException();
}

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;
    public NotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("{notificationId:int}/resend")]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return notification is null ? NotFound() : Ok(new { notificationId = notification.Id, status = notification.ProviderStatus });
        }
        catch (RequestValidationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{notificationId:int}/content")]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        try { return await _service.DisposeContentAsync(notificationId, cancellationToken) ? NoContent() : NotFound(); }
        catch (TwilioProviderException) { return StatusCode(StatusCodes.Status502BadGateway, new { error = "Provider content disposal could not be confirmed." }); }
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await _service.ReconcileAsync(from, to, cancellationToken);
            return Ok(new { report.From, report.To, messages = report.Messages });
        }
        catch (RequestValidationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (TwilioProviderException) { return StatusCode(StatusCodes.Status502BadGateway, new { error = "Provider reconciliation data could not be read." }); }
    }
}

public sealed class RegisterContactNumberRequest
{
    public string Number { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest { public int CatalogItemId { get; set; } public int Quantity { get; set; } }
public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
public sealed class ResendNotificationRequest { public string IdempotencyKey { get; set; } = string.Empty; }

public sealed record NotificationResponse(int NotificationId, string Type, string? Content, string? ProviderMessageSid,
    string Status, int? ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? ContentDisposedAt)
{
    public static NotificationResponse From(OrderNotification x) => new(x.Id, x.Kind.ToString(), x.Content,
        x.ProviderMessageSid, x.ProviderStatus, x.ProviderErrorCode, x.CreatedAt, x.ScheduledFor, x.ContentDisposedAt);
}
