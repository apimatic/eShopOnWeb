using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrderNotificationEndpoints : ControllerBase
{
    private readonly IOrderNotificationService _service;

    public OrderNotificationEndpoints(IOrderNotificationService service) => _service = service;

    [HttpPost("api/contact-numbers")]
    public async Task<IResult> RegisterContactNumber(RegisterContactNumberRequest request, CancellationToken ct)
    {
        try
        {
            var contact = await _service.RegisterContactNumberAsync(CurrentBuyer(), request.PhoneNumber, ct);
            return Results.Created($"/api/contact-numbers/{contact.ContactNumberId}", new
            {
                contactNumberId = contact.ContactNumberId,
                phoneNumber = contact.PhoneNumber,
                registeredAt = contact.RegisteredAt
            });
        }
        catch (ContactNumberValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (NotificationProviderException ex)
        {
            return ProviderFailure(ex);
        }
    }

    [HttpGet("api/contact-numbers")]
    public async Task<IResult> GetContactNumbers(CancellationToken ct) =>
        Results.Ok(await _service.GetContactNumbersAsync(CurrentBuyer(), ct));

    [HttpDelete("api/contact-numbers/{contactNumberId:int}")]
    public async Task<IResult> DeleteContactNumber(int contactNumberId, CancellationToken ct)
    {
        try
        {
            return await _service.DeleteContactNumberAsync(CurrentBuyer(), contactNumberId, ct)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (NotificationProviderException ex)
        {
            return ProviderFailure(ex);
        }
    }

    [HttpPost("api/orders")]
    public async Task<IResult> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Items is null || request.ShippingAddress is null)
            {
                return Results.BadRequest(new { error = "Items and shippingAddress are required." });
            }

            var address = request.ShippingAddress;
            var orderId = await _service.PlaceOrderAsync(
                CurrentBuyer(),
                request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
                ct);
            return Results.Created($"/api/orders/{orderId}", new { orderId });
        }
        catch (Exception ex) when (ex is OrderRequestValidationException or ArgumentException)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/orders/{orderId:int}/dispatch")]
    public async Task<IResult> DispatchOrder(int orderId, CancellationToken ct)
    {
        try
        {
            return await _service.DispatchOrderAsync(orderId, ct) ? Results.Ok(new { orderId, status = "Dispatched" }) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/orders/{orderId:int}/cancel")]
    public async Task<IResult> CancelOrder(int orderId, CancellationToken ct) =>
        await _service.CancelOrderAsync(orderId, ct) ? Results.Ok(new { orderId, status = "Cancelled" }) : Results.NotFound();

    [HttpGet("api/my-orders")]
    public async Task<IResult> GetMyOrders(CancellationToken ct) =>
        Results.Ok(await _service.GetMyOrdersAsync(CurrentBuyer(), ct));

    [HttpGet("api/orders/{orderId:int}/notifications")]
    public async Task<IResult> GetOrderNotifications(int orderId, CancellationToken ct)
    {
        var notifications = await _service.GetOrderNotificationsAsync(CurrentBuyer(), orderId, ct);
        return notifications is null ? Results.NotFound() : Results.Ok(notifications);
    }

    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/notifications/{notificationId:int}/resend")]
    public async Task<IResult> Resend(int notificationId, ResendNotificationRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.ResendAsync(notificationId, request.IdempotencyKey, ct);
            return result.HasValue ? Results.Created($"/api/notifications/{result.Value}", new { notificationId = result.Value }) : Results.NotFound();
        }
        catch (NotificationActionException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpDelete("api/notifications/{notificationId:int}/content")]
    public async Task<IResult> DisposeContent(int notificationId, CancellationToken ct)
    {
        try
        {
            return await _service.DisposeContentAsync(notificationId, ct) ? Results.NoContent() : Results.NotFound();
        }
        catch (NotificationProviderException ex)
        {
            return ProviderFailure(ex);
        }
    }

    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/notifications/reconciliation")]
    public async Task<IResult> Reconcile([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await _service.ReconcileAsync(from, to, ct));
        }
        catch (NotificationActionException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (NotificationProviderException ex)
        {
            return ProviderFailure(ex);
        }
    }

    private string CurrentBuyer() => User.Identity?.Name ?? throw new UnauthorizedAccessException();

    private static IResult ProviderFailure(NotificationProviderException ex) => ex.StatusCode switch
    {
        429 => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
        401 or 403 => Results.StatusCode(StatusCodes.Status502BadGateway),
        >= 400 and < 500 => Results.BadRequest(new { error = ex.Message }),
        _ => Results.StatusCode(StatusCodes.Status502BadGateway)
    };
}

public sealed record RegisterContactNumberRequest(string PhoneNumber);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderLineRequest>? Items, ShippingAddressRequest? ShippingAddress);
public sealed record PlaceOrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record ResendNotificationRequest(string IdempotencyKey);
