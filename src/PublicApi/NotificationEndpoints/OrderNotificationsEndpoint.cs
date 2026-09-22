using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of each message. Shopper-scoped: a non-admin caller
/// only sees their own order's notifications. Each entry carries its own notificationId (what the
/// operator endpoints act on).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class OrderNotificationsEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderNotificationsResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public OrderNotificationsEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpGet("api/orders/{orderId}/notifications")]
    [SwaggerOperation(
        Summary = "The notifications sent for an order and their outcomes",
        Description = "What was sent for this order, and what became of each message",
        OperationId = "orders.notifications",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderNotificationsResponse>> HandleAsync([FromRoute] int orderId, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        var isAdmin = User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var notifications = await _orderNotificationService.GetOrderNotificationsAsync(orderId, buyerId, isAdmin, cancellationToken);
        if (notifications is null)
        {
            return NotFound();
        }

        return Ok(new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
        });
    }
}
