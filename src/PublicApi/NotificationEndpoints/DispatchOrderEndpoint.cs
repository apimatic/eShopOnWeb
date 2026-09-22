using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: mark an order dispatched. The shopper is told it is on its way and a delivery
/// follow-up is queued with the provider a few days out. Restricted to administrators.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DispatchOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderActionResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public DispatchOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders/{orderId}/dispatch")]
    [SwaggerOperation(
        Summary = "Marks an order dispatched (operator)",
        Description = "Marks an order dispatched, notifies the shopper, and queues a delivery follow-up",
        OperationId = "orders.dispatch",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderActionResponse>> HandleAsync([FromRoute] int orderId, CancellationToken cancellationToken = default)
    {
        var outcome = await _orderNotificationService.DispatchOrderAsync(orderId, cancellationToken);
        return outcome switch
        {
            OrderActionOutcome.Applied => Ok(new OrderActionResponse { OrderId = orderId, Status = "Dispatched" }),
            OrderActionOutcome.NoChange => Conflict("Order cannot be dispatched from its current state."),
            _ => NotFound()
        };
    }
}
