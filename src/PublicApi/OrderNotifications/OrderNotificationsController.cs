using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class OrderNotificationsController : ControllerBase
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly IOrderNotificationService _service;

    public OrderNotificationsController(IOrderNotificationService service) => _service = service;

    [HttpPost("api/contact-numbers")]
    [ProducesResponseType(typeof(ContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterContactNumber(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var contact = await _service.RegisterContactNumberAsync(BuyerId(), request.MobileNumber,
                cancellationToken);
            return Created($"/api/contact-numbers/{contact.Id}",
                new ContactNumberResponse(contact.Id, contact.CanonicalNumber));
        }
        catch (OrderNotificationValidationException ex) { return ValidationProblem(ex.Message); }
        catch (Exception ex) when (IsProviderFailure(ex)) { return ProviderUnavailable(); }
    }

    [HttpGet("api/contact-numbers")]
    public async Task<ActionResult<IReadOnlyList<ContactNumberResponse>>> GetContactNumbers(
        CancellationToken cancellationToken)
    {
        var contacts = await _service.GetContactNumbersAsync(BuyerId(), cancellationToken);
        return Ok(contacts.Select(x => new ContactNumberResponse(x.Id, x.CanonicalNumber)));
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
        catch (Exception ex) when (IsProviderFailure(ex)) { return ProviderUnavailable(); }
    }

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var lines = request.Items?.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList()
                ?? new List<OrderLineInput>();
            var order = await _service.PlaceOrderAsync(BuyerId(), lines, cancellationToken);
            return Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id, order.Status.ToString()));
        }
        catch (OrderNotificationValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/orders/{orderId:int}/dispatch")]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.DispatchOrderAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (OrderNotificationConflictException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
    }

    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/orders/{orderId:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _service.CancelOrderAsync(orderId, cancellationToken);
            return order is null ? NotFound() : Ok(new { orderId = order.Id, status = order.Status.ToString() });
        }
        catch (OrderNotificationConflictException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
    }

    [HttpGet("api/my-orders")]
    public async Task<IActionResult> GetMyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetOrdersAsync(BuyerId(), cancellationToken));

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> GetOrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var notifications = await _service.GetNotificationsAsync(BuyerId(), orderId, cancellationToken);
        return notifications is null ? NotFound() : Ok(notifications.Select(ToResponse));
    }

    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/notifications/{notificationId:int}/resend")]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> ResendNotification(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return notification is null
                ? NotFound()
                : Created($"/api/orders/{notification.OrderId}/notifications",
                    new ResendNotificationResponse(notification.Id));
        }
        catch (OrderNotificationValidationException ex) { return ValidationProblem(ex.Message); }
        catch (OrderNotificationConflictException ex) { return Conflict(new ProblemDetails { Detail = ex.Message }); }
    }

    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpDelete("api/notifications/{notificationId:int}/content")]
    public async Task<IActionResult> DisposeNotificationContent(int notificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _service.DisposeContentAsync(notificationId, cancellationToken) ? NoContent() : NotFound();
        }
        catch (Exception ex) when (IsProviderFailure(ex)) { return ProviderUnavailable(); }
    }

    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/notifications/reconciliation")]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ReconcileAsync(from, to, cancellationToken));
        }
        catch (OrderNotificationValidationException ex) { return ValidationProblem(ex.Message); }
        catch (Exception ex) when (IsProviderFailure(ex)) { return ProviderUnavailable(); }
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private static NotificationResponse ToResponse(OrderNotification value) => new(value.Id, value.Kind.ToString(),
        value.DeliveryStatus.ToString(), value.ProviderMessageSid, value.ProviderStatus, value.ProviderErrorCode,
        value.ProviderErrorMessage, value.ContentDisposed ? null : value.Content, value.ContentDisposed,
        value.CreatedAt, value.ProviderSentAt, value.ScheduledFor, value.SourceNotificationId);

    private static bool IsProviderFailure(Exception exception) =>
        exception is MessagingProviderException or HttpRequestException or TaskCanceledException;

    private ObjectResult ValidationProblem(string detail) => Problem(detail: detail,
        statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

    private ObjectResult ProviderUnavailable() => Problem(
        detail: "The messaging provider could not complete the request.",
        statusCode: StatusCodes.Status502BadGateway, title: "Messaging provider unavailable");
}

public sealed record RegisterContactNumberRequest(string MobileNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string MobileNumber);
public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);
public sealed record CreateOrderResponse(int OrderId, string Status);
public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);
public sealed record NotificationResponse(int NotificationId, string Kind, string DeliveryStatus,
    string? ProviderMessageSid, string? ProviderStatus, int? ProviderErrorCode, string? ProviderErrorMessage,
    string? Content, bool ContentDisposed, DateTimeOffset CreatedAt, DateTimeOffset? ProviderSentAt,
    DateTimeOffset? ScheduledFor, int? SourceNotificationId);
