using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class OrderNotificationsController : ControllerBase
{
    private readonly IOrderNotificationService _service;

    public OrderNotificationsController(IOrderNotificationService service) => _service = service;

    [HttpPost("api/contact-numbers")]
    [ProducesResponseType<ContactNumberView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterContactNumber(RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RegisterContactNumberAsync(BuyerId, request.PhoneNumber,
            request.CountryCode, cancellationToken);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Failure(result);
    }

    [HttpGet("api/contact-numbers")]
    [ProducesResponseType<IReadOnlyList<ContactNumberView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetContactNumbers(CancellationToken cancellationToken) =>
        Ok(await _service.GetContactNumbersAsync(BuyerId, cancellationToken));

    [HttpDelete("api/contact-numbers/{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        var result = await _service.RemoveContactNumberAsync(BuyerId, contactNumberId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpPost("api/orders")]
    [ProducesResponseType<OrderView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.PlaceOrderAsync(BuyerId, request.Items, request.ShippingAddress, cancellationToken);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Failure(result);
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.DispatchOrderAsync(orderId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.CancelOrderAsync(orderId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType<IReadOnlyList<OrderView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetOrdersAsync(BuyerId, cancellationToken));

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IActionResult> GetOrderNotifications(int orderId, CancellationToken cancellationToken)
    {
        var result = await _service.GetNotificationsAsync(BuyerId, orderId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    [HttpPost("api/notifications/{notificationId:int}/resend")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<NotificationView>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Failure(result);
    }

    [HttpDelete("api/notifications/{notificationId:int}/content")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DeleteContent(int notificationId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteContentAsync(notificationId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpGet("api/notifications/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconcile([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var result = await _service.ReconcileAsync(from, to, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Failure(result);
    }

    private string BuyerId => User.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private ObjectResult Failure<T>(ServiceResult<T> result) => result.Failure switch
    {
        ServiceFailure.NotFound => Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
        ServiceFailure.Invalid => Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
        ServiceFailure.Conflict => Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
        ServiceFailure.ProviderUnavailable => Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Problem("The operation failed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}

public sealed record RegisterContactNumberRequest(string PhoneNumber, string? CountryCode = null);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineInput>? Items, ShippingAddressInput? ShippingAddress = null);
public sealed record ResendNotificationRequest(string IdempotencyKey);
