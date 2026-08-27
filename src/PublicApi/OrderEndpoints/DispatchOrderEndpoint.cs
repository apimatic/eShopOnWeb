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

public class OrderStatusChangeResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Marks an order dispatched (operator). The shopper is told it is on its way
/// and a delivery follow-up is queued with the provider for a few days later.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DispatchOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusChangeResponse>
{
    private readonly IRepository<Order> _orders;
    private readonly IOrderNotificationService _notifications;

    public DispatchOrderEndpoint(IRepository<Order> orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    [HttpPost("api/orders/{orderId}/dispatch")]
    [SwaggerOperation(Summary = "Marks an order dispatched (operator)", Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderStatusChangeResponse>> HandleAsync(
        [FromRoute(Name = "orderId")] int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return NotFound();

        if (order.Status != OrderStatus.Placed)
        {
            return Conflict(new { error = $"Order {orderId} is {order.Status}; only a placed order can be dispatched." });
        }

        order.MarkDispatched();
        await _orders.UpdateAsync(order, cancellationToken);

        // Never fails the dispatch: notification errors are recorded, not thrown.
        await _notifications.NotifyOrderDispatchedAsync(order, cancellationToken);

        return new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}
