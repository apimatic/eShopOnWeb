using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrderNotificationsController : ControllerBase
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly OrderNotificationApplicationService _service;

    public OrderNotificationsController(OrderNotificationApplicationService service)
    {
        _service = service;
    }

    [HttpPost("contact-numbers")]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterContactNumberResponse>> RegisterContactNumber(
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RegisterContactAsync(BuyerId(), request.Number, cancellationToken);
        return Created($"/api/contact-numbers/{response.ContactNumberId}", response);
    }

    [HttpGet("contact-numbers")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactNumberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContactNumberResponse>>> GetContactNumbers(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetContactsAsync(BuyerId(), cancellationToken));
    }

    [HttpDelete("contact-numbers/{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        await _service.DeleteContactAsync(BuyerId(), contactNumberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(PlaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DispatchOrder(int orderId, CancellationToken cancellationToken)
    {
        await _service.DispatchOrderAsync(orderId, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelOrder(int orderId, CancellationToken cancellationToken)
    {
        await _service.CancelOrderAsync(orderId, cancellationToken);
        return NoContent();
    }

    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<MyOrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> GetMyOrders(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetMyOrdersAsync(BuyerId(), cancellationToken));
    }

    [HttpGet("orders/{orderId:int}/notifications")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> GetOrderNotifications(
        int orderId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.GetOrderNotificationsAsync(BuyerId(), orderId, cancellationToken));
    }

    [HttpPost("notifications/{notificationId:int}/resend")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ResendNotificationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ResendNotificationResponse>> ResendNotification(
        int notificationId,
        ResendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        return Created($"/api/notifications/{response.NotificationId}", response);
    }

    [HttpDelete("notifications/{notificationId:int}/content")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeNotificationContent(int notificationId, CancellationToken cancellationToken)
    {
        await _service.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("notifications/reconciliation")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconcile(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ReconcileAsync(from, to, cancellationToken));
    }

    private string BuyerId()
    {
        return User.FindFirstValue(ClaimTypes.Name)
            ?? throw new NotificationApiException(StatusCodes.Status401Unauthorized, "An authenticated shopper is required.");
    }
}
