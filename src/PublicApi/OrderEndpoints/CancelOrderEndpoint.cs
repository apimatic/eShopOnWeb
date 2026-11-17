using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<OrderDto>
{
    private readonly IPaymentService _paymentService;

    public CancelOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Cancel an order before fulfilment",
        Description = "Operator action. Releases the shopper's authorized funds if the order has not been captured.",
        OperationId = "orders.cancel",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orderId = PaymentApiMapper.RouteId(RouteData, "orderId");
            var order = await _paymentService.CancelOrderAsync(orderId, cancellationToken);
            return Ok(order);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}
