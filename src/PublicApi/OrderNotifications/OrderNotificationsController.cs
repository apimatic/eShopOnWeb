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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class OrderNotificationsController : ControllerBase
{
    private readonly OrderNotificationService _service;

    public OrderNotificationsController(OrderNotificationService service)
    {
        _service = service;
    }

    [HttpPost("api/contact-numbers")]
    [ProducesResponseType(typeof(CreateContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterContactNumber(
        [FromBody] CreateContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RegisterContactNumberAsync(
                BuyerId(),
                request.Number,
                request.CountryCode,
                cancellationToken);
            if (result.Contact == null)
            {
                return BadRequest(new { errors = result.ValidationErrors });
            }

            var response = new CreateContactNumberResponse(result.Contact.Id, result.Contact.Value);
            return Created($"/api/contact-numbers/{result.Contact.Id}", response);
        }
        catch (SmsProviderException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Phone-number validation is temporarily unavailable.");
        }
    }

    [HttpGet("api/contact-numbers")]
    public async Task<ActionResult<IReadOnlyList<ContactNumberResponse>>> GetContactNumbers(CancellationToken cancellationToken)
    {
        var contacts = await _service.GetContactNumbersAsync(BuyerId(), cancellationToken);
        return Ok(contacts.Select(x => new ContactNumberResponse(x.Id, x.Value, x.CreatedAt)));
    }

    [HttpDelete("api/contact-numbers/{contactNumberId:int}")]
    public async Task<IActionResult> DeleteContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DeleteContactNumberAsync(BuyerId(), contactNumberId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (SmsProviderException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Scheduled notifications could not be made safe for removal.");
        }
    }

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.PlaceOrderAsync(BuyerId(), request.Items, request.ShippingAddress, cancellationToken);
            return Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id));
        }
        catch (OrderInputException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _service.DispatchOrderAsync(orderId, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return Conflict(new { error = "A cancelled order cannot be dispatched." });
        }
        return Ok(new OrderStateResponse(order.Id, order.Status.ToString()));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.CancelOrderAsync(orderId, cancellationToken);
            return order == null
                ? NotFound()
                : Ok(new OrderStateResponse(order.Id, order.Status.ToString()));
        }
        catch (FollowUpCancellationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (SmsProviderException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The scheduled follow-up could not be cancelled safely.");
        }
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderSummary>>> GetMyOrders(CancellationToken cancellationToken)
        => Ok(await _service.GetMyOrdersAsync(BuyerId(), cancellationToken));

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> GetOrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _service.GetOrderNotificationsAsync(BuyerId(), orderId, cancellationToken);
        return notifications == null
            ? NotFound()
            : Ok(notifications.Select(MapNotification));
    }

    [HttpPost("api/notifications/{notificationId:int}/resend")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> ResendNotification(
        int notificationId,
        [FromBody] ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            if (result == null)
            {
                return NotFound();
            }

            var response = new ResendNotificationResponse(result.Notification.Id);
            return result.WasCreated
                ? Created($"/api/notifications/{result.Notification.Id}", response)
                : Ok(response);
        }
        catch (OrderInputException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ResendNotAllowedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpDelete("api/notifications/{notificationId:int}/content")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DeleteNotificationContent(int notificationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DeleteContentAsync(notificationId, cancellationToken)
                ? NoContent()
                : NotFound();
        }
        catch (SmsProviderException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The provider could not dispose of the message content.");
        }
    }

    [HttpGet("api/notifications/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconcile(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ReconcileAsync(from, to, cancellationToken));
        }
        catch (OrderInputException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (SmsProviderException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Provider reconciliation is temporarily unavailable.");
        }
    }

    private string BuyerId()
        => User.Identity?.Name ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private static NotificationResponse MapNotification(OrderNotification value)
        => new(
            value.Id,
            value.OrderId,
            value.Kind.ToString(),
            value.Body,
            value.ProviderMessageSid,
            value.ProviderStatus,
            value.ProviderErrorCode,
            value.ScheduledFor,
            value.CreatedAt,
            value.UpdatedAt,
            value.ContentDeletedAt,
            value.ResendOfNotificationId);
}

public sealed class CreateContactNumberRequest
{
    public string Number { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public sealed record CreateContactNumberResponse(int ContactNumberId, string Number);
public sealed record ContactNumberResponse(int ContactNumberId, string Number, DateTimeOffset CreatedAt);

public sealed class CreateOrderRequest
{
    public List<OrderLineInput> Items { get; set; } = new();
    public AddressInput ShippingAddress { get; set; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

public sealed record CreateOrderResponse(int OrderId);
public sealed record OrderStateResponse(int OrderId, string Status);

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record NotificationResponse(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ContentDeletedAt,
    int? ResendOfNotificationId);
