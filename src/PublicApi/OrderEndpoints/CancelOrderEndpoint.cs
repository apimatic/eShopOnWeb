using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before it is fulfilled. If a hold is in place it is
/// released (voided) so no money ever moves.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<CancelOrderRequest>
    .WithActionResult<CancelOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(
        Summary = "Cancels an order",
        Description = "Operator action: cancels an order before fulfilment and releases any held funds",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<CancelOrderResponse>> HandleAsync(CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new CancelOrderResponse(request.CorrelationId());

        var order = await _orderPaymentService.CancelOrderAsync(request.OrderId, cancellationToken);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();

        return response;
    }
}
