using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing (voiding) the held funds so no
/// money ever moved.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CancelOrderEndpoint : EndpointBaseAsync
    .WithRequest<OrderIdRouteRequest>
    .WithActionResult<OrderSummaryDto>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ApplicationCore.Models.PayPal.PayPalSettings _settings;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService,
        ApplicationCore.Models.PayPal.PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _settings = settings;
    }

    [HttpPost("api/orders/{orderId}/cancel")]
    [SwaggerOperation(
        Summary = "Cancels an order before fulfilment (operator)",
        Description = "Voids the authorization so the shopper's held funds are released.",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<OrderSummaryDto>> HandleAsync(
        OrderIdRouteRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.CancelOrderAsync(request.OrderId, cancellationToken);
        return Ok(PaymentMappings.ToSummary(order, _settings.Currency));
    }
}
