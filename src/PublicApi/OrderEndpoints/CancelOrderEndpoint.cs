using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order. The shopper is told, and any not-yet-sent delivery
/// follow-up is called off with the provider so it never reaches them.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusChangeResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(
        Summary = "Cancels an order (operator)",
        Description = "Notifies the shopper and cancels any scheduled delivery follow-up before it goes out",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<OrderStatusChangeResponse>> HandleAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new NotFoundException("Order not found.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new ConflictException("The order is already cancelled.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _orderNotificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        return new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}
