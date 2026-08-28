using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class NotificationApiController : ControllerBase
{
    protected string BuyerId => User.Identity?.Name
        ?? throw new ApiProblemException(401, "The authenticated user has no name claim.");

    protected async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiProblemException ex)
        {
            return Problem(statusCode: ex.StatusCode, detail: ex.Message);
        }
    }
}

public sealed class ContactNumbersController : NotificationApiController
{
    private readonly CommerceNotificationService _service;
    public ContactNumbersController(CommerceNotificationService service) => _service = service;

    [HttpPost("/api/contact-numbers")]
    public Task<IActionResult> Register(RegisterContactNumberRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var contact = await _service.RegisterContactAsync(BuyerId, request.MobileNumber, cancellationToken);
            return Created($"/api/contact-numbers/{contact.Id}", new
            {
                contactNumberId = contact.Id,
                mobileNumber = contact.Number
            });
        });

    [HttpGet("/api/contact-numbers")]
    public Task<IActionResult> List(CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var contacts = await _service.GetContactsAsync(BuyerId, cancellationToken);
        return Ok(new
        {
            contactNumbers = contacts.Select(x => new
            {
                contactNumberId = x.Id,
                mobileNumber = x.Number,
                createdAt = x.CreatedAt
            })
        });
    });

    [HttpDelete("/api/contact-numbers/{contactNumberId:int}")]
    public Task<IActionResult> Delete(int contactNumberId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await _service.DeleteContactAsync(BuyerId, contactNumberId, cancellationToken);
            return NoContent();
        });
}

public sealed class OrdersNotificationController : NotificationApiController
{
    private readonly CommerceNotificationService _service;
    public OrdersNotificationController(CommerceNotificationService service) => _service = service;

    [HttpPost("/api/orders")]
    public Task<IActionResult> Place(PlaceOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            if (request.Items == null || request.ShippingAddress == null ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.Street) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.City) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.Country) ||
                string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
                throw new ApiProblemException(400, "Items and a complete shipping address are required.");

            var order = await _service.PlaceOrderAsync(BuyerId,
                request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
                    request.ShippingAddress.State ?? string.Empty, request.ShippingAddress.Country,
                    request.ShippingAddress.ZipCode), cancellationToken);
            return Created($"/api/orders/{order.Id}", new { orderId = order.Id });
        });

    [HttpPost("/api/orders/{orderId:int}/dispatch")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<IActionResult> Dispatch(int orderId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var order = await _service.DispatchAsync(orderId, cancellationToken);
            return Ok(new { orderId = order.Id, status = order.FulfillmentStatus.ToString() });
        });

    [HttpPost("/api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var order = await _service.CancelAsync(orderId, cancellationToken);
            return Ok(new { orderId = order.Id, status = order.FulfillmentStatus.ToString() });
        });

    [HttpGet("/api/my-orders")]
    public Task<IActionResult> MyOrders(CancellationToken cancellationToken) => ExecuteAsync(async () =>
        Ok(new { orders = await _service.GetMyOrdersAsync(BuyerId, cancellationToken) }));

    [HttpGet("/api/orders/{orderId:int}/notifications")]
    public Task<IActionResult> Notifications(int orderId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(new
        {
            notifications = await _service.GetOrderNotificationsAsync(BuyerId, orderId, cancellationToken)
        }));
}

public sealed class NotificationOperationsController : NotificationApiController
{
    private readonly CommerceNotificationService _service;
    public NotificationOperationsController(CommerceNotificationService service) => _service = service;

    [HttpPost("/api/notifications/{notificationId:int}/resend")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<IActionResult> Resend(int notificationId, ResendNotificationRequest request,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        var producedId = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created($"/api/notifications/{producedId}", new { notificationId = producedId });
    });

    [HttpDelete("/api/notifications/{notificationId:int}/content")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<IActionResult> DisposeContent(int notificationId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await _service.DisposeContentAsync(notificationId, cancellationToken);
            return NoContent();
        });

    [HttpGet("/api/notifications/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<IActionResult> Reconcile([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        if (from == null || to == null) throw new ApiProblemException(400, "ISO-8601 from and to values are required.");
        return Ok(await _service.ReconcileAsync(from.Value, to.Value, cancellationToken));
    });
}

public sealed class RegisterContactNumberRequest
{
    public string MobileNumber { get; set; } = string.Empty;
}

public sealed class PlaceOrderRequest
{
    public List<OrderLineRequest>? Items { get; set; }
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
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
