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
/// Operator action: marks an order dispatched, tells the shopper it is on its
/// way, and queues a delivery follow-up message with the provider for a few
/// days later.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DispatchOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderStatusResponse>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public DispatchOrderEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    [HttpPost("api/orders/{orderId}/dispatch")]
    [SwaggerOperation(
        Summary = "Marks an order dispatched",
        Description = "Marks an order dispatched and notifies the shopper",
        OperationId = "orders.dispatch",
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
            order.MarkDispatched();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        await _orderRepository.UpdateAsync(order, cancellationToken);

        await _notificationService.NotifyOrderDispatchedAsync(order, cancellationToken);

        return new OrderStatusResponse { OrderId = order.Id, Status = order.Status.ToString() };
    }
}

public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
