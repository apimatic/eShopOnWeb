using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Cancels an order (operator). The shopper is told, and any delivery follow-up
/// still queued with the provider is called off so it never reaches them.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusChangeResponse>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public CancelOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(Summary = "Cancels an order (operator)", Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderStatusChangeResponse>> HandleAsync(
        [FromRoute(Name = "orderId")] int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status == OrderStatus.Cancelled)
        {
            return Conflict(new { error = $"Order {orderId} is already cancelled." });
        }

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);

        // Never fails the cancellation: notification errors are recorded, not thrown.
        await _notifications.NotifyOrderCancelledAsync(order, cancellationToken);

        return new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}
