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

public class FulfilOrderEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<OrderDto>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Fulfil an order and capture its authorized payment",
        Description = "Operator action. Captures the full authorized amount and records Finix's captured amount, fee and net proceeds.",
        OperationId = "orders.fulfil",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<OrderDto>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orderId = PaymentApiMapper.RouteId(RouteData, "orderId");
            var order = await _paymentService.FulfilOrderAsync(orderId, cancellationToken);
            return Ok(order);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}
