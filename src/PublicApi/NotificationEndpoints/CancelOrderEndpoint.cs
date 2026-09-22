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
/// Operator action: cancel an order. The shopper is told, and any delivery follow-up that has not
/// yet gone out is called off so it never reaches them. Restricted to administrators.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithActionResult<OrderActionResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public CancelOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(
        Summary = "Cancels an order (operator)",
        Description = "Cancels an order, notifies the shopper, and calls off any pending delivery follow-up",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderActionResponse>> HandleAsync([FromRoute] int orderId, CancellationToken cancellationToken = default)
    {
        var outcome = await _orderNotificationService.CancelOrderAsync(orderId, cancellationToken);
        return outcome switch
        {
            OrderActionOutcome.Applied => Ok(new OrderActionResponse { OrderId = orderId, Status = "Cancelled" }),
            OrderActionOutcome.NoChange => Conflict("Order is already cancelled."),
            _ => NotFound()
        };
    }
}
