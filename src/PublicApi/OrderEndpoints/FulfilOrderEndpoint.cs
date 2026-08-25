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
/// Operator action: marks the order fulfilled and captures the held payment. Renews the
/// authorization first if it has gone stale; if PayPal can no longer renew it, the failure is
/// reported for an operator to act on rather than silently failing.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FulfilOrderEndpoint : EndpointBaseAsync
    .WithRequest<FulfilOrderRequest>
    .WithActionResult<FulfilOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/fulfil")]
    [SwaggerOperation(
        Summary = "Fulfils an order",
        Description = "Operator action: captures the held payment and marks the order fulfilled",
        OperationId = "orders.fulfil",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<FulfilOrderResponse>> HandleAsync(FulfilOrderRequest request, CancellationToken cancellationToken = default)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var payment = await _orderPaymentService.FulfilOrderAsync(request.OrderId, cancellationToken);

        response.OrderId = request.OrderId;
        response.Payment = payment.ToDto();

        return response;
    }
}
