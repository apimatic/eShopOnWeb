using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrderNotificationsController(OrderNotificationService service) : ControllerBase
{
    [HttpPost("api/contact-numbers")]
    [ProducesResponseType<ContactNumberCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContactNumberCreatedResponse>> RegisterContactNumber(
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        var id = await service.RegisterContactNumberAsync(ShopperId(), request.PhoneNumber, cancellationToken);
        return Created("api/contact-numbers", new ContactNumberCreatedResponse(id));
    }

    [HttpGet("api/contact-numbers")]
    public Task<IReadOnlyList<ContactNumberView>> GetContactNumbers(CancellationToken cancellationToken) =>
        service.GetContactNumbersAsync(ShopperId(), cancellationToken);

    [HttpDelete("api/contact-numbers/{contactNumberId:int}")]
    public async Task<IActionResult> DeleteContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        await service.DeleteContactNumberAsync(ShopperId(), contactNumberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("api/orders")]
    [ProducesResponseType<OrderCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var lines = request.Items.ConvertAll(x => new OrderLineInput(x.CatalogItemId, x.Quantity));
        var address = new ShippingAddressInput(request.ShippingAddress.Street, request.ShippingAddress.City,
            request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var id = await service.PlaceOrderAsync(ShopperId(), lines, address, cancellationToken);
        return Created($"api/orders/{id}", new OrderCreatedResponse(id));
    }

    [HttpPost("api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        await service.DispatchOrderAsync(orderId, cancellationToken);
        return Ok();
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        await service.CancelOrderAsync(orderId, cancellationToken);
        return Ok();
    }

    [HttpGet("api/my-orders")]
    public Task<IReadOnlyList<OrderView>> GetMyOrders(CancellationToken cancellationToken) =>
        service.GetMyOrdersAsync(ShopperId(), cancellationToken);

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public Task<IReadOnlyList<NotificationView>> GetOrderNotifications(int orderId,
        CancellationToken cancellationToken) =>
        service.GetOrderNotificationsAsync(ShopperId(), orderId, cancellationToken);

    [HttpPost("api/notifications/{notificationId:int}/resend")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<NotificationCreatedResponse>> Resend(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken)
    {
        var id = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Ok(new NotificationCreatedResponse(id));
    }

    [HttpDelete("api/notifications/{notificationId:int}/content")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken)
    {
        await service.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/notifications/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationView> Reconcile([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) => service.ReconcileAsync(from, to, cancellationToken);

    private string ShopperId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new NotificationOperationException(401, "The authenticated identity has no name claim.");
}

public sealed class RegisterContactNumberRequest
{
    [Required]
    public string PhoneNumber { get; init; } = string.Empty;
}

public sealed record ContactNumberCreatedResponse(int ContactNumberId);

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<OrderLineRequest> Items { get; init; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public sealed record OrderCreatedResponse(int OrderId);

public sealed class ResendNotificationRequest
{
    [Required]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record NotificationCreatedResponse(int NotificationId);
