using System;
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
    private readonly OrderNotificationService _service;

    public OrderNotificationsController(OrderNotificationService service) => _service = service;

    [HttpPost("contact-numbers")]
    [ProducesResponseType(typeof(RegisterContactNumberResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterContactNumberResponse>> RegisterContactNumber(
        RegisterContactNumberRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RegisterContactNumberAsync(BuyerId, request, cancellationToken);
        return Created($"/api/contact-numbers/{response.ContactNumberId}", response);
    }

    [HttpGet("contact-numbers")]
    public Task<ContactNumbersResponse> GetContactNumbers(CancellationToken cancellationToken) =>
        _service.GetContactNumbersAsync(BuyerId, cancellationToken);

    [HttpDelete("contact-numbers/{contactNumberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteContactNumber(int contactNumberId, CancellationToken cancellationToken)
    {
        await _service.DeleteContactNumberAsync(BuyerId, contactNumberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.CreateOrderAsync(BuyerId, request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/dispatch")]
    [Authorize(Roles = AdministratorRole)]
    public Task<OrderActionResponse> DispatchOrder(int orderId, CancellationToken cancellationToken) =>
        _service.DispatchOrderAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = AdministratorRole)]
    public Task<OrderActionResponse> CancelOrder(int orderId, CancellationToken cancellationToken) =>
        _service.CancelOrderAsync(orderId, cancellationToken);

    [HttpGet("my-orders")]
    public Task<MyOrdersResponse> GetMyOrders(CancellationToken cancellationToken) =>
        _service.GetMyOrdersAsync(BuyerId, cancellationToken);

    [HttpGet("orders/{orderId:int}/notifications")]
    public Task<OrderNotificationsResponse> GetOrderNotifications(int orderId, CancellationToken cancellationToken) =>
        _service.GetOrderNotificationsAsync(BuyerId, orderId, cancellationToken);

    [HttpPost("notifications/{notificationId:int}/resend")]
    [Authorize(Roles = AdministratorRole)]
    public Task<ResendNotificationResponse> ResendNotification(int notificationId,
        ResendNotificationRequest request, CancellationToken cancellationToken) =>
        _service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);

    [HttpDelete("notifications/{notificationId:int}/content")]
    [Authorize(Roles = AdministratorRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisposeNotificationContent(int notificationId,
        CancellationToken cancellationToken)
    {
        await _service.DisposeContentAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("notifications/reconciliation")]
    [Authorize(Roles = AdministratorRole)]
    public Task<ReconciliationResponse> Reconcile([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        _service.ReconcileAsync(from, to, cancellationToken);

    private string BuyerId => User.Identity?.Name
        ?? throw new ApiProblemException(StatusCodes.Status401Unauthorized, "An authenticated user identity is required.");
}
