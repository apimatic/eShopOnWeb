using System;
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
/// Operator action: cancels an order, tells the shopper, and calls off any
/// delivery follow-up that has not yet gone out.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public CancelOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(
        Summary = "Cancels an order",
        Description = "Cancels an order, notifies the shopper and cancels any pending follow-up message",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<OrderStatusResponse>> HandleAsync(
        [FromRoute(Name = "orderId")] int request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(request, cancellationToken);
        if (order == null)
        {
            return NotFound();
        }

        try
        {
            order.MarkCancelled();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderCancelledAsync(order, cancellationToken);

        return new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}
