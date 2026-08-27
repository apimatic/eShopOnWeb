using System;
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
/// Operator action: marks an order dispatched. The shopper is told it is on its way, and a
/// delivery follow-up message is queued with the provider for a few days later.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DispatchOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusChangeResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders/{orderId}/dispatch")]
    [SwaggerOperation(
        Summary = "Marks an order dispatched (operator)",
        Description = "Notifies the shopper and queues a delivery follow-up message with the provider",
        OperationId = "orders.dispatch",
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

        if (order.Status != OrderStatus.Placed)
        {
            throw new ConflictException($"Only a placed order can be dispatched; this order is {order.Status}.");
        }

        order.MarkDispatched();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _orderNotificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return new OrderStatusChangeResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}

public class OrderStatusChangeResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
