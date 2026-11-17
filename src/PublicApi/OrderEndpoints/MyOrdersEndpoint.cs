using System.Collections.Generic;
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

public class MyOrdersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<IReadOnlyList<OrderDto>>
{
    private readonly IPaymentService _paymentService;

    public MyOrdersEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("api/my-orders")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "List the signed-in shopper's orders",
        Description = "Returns the caller's orders together with their payment, capture and refund state.",
        OperationId = "orders.mine",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<IReadOnlyList<OrderDto>>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buyerId = PaymentApiMapper.BuyerId(User);
            var orders = await _paymentService.GetOrdersAsync(buyerId, cancellationToken);
            return Ok(orders);
        }
        catch (System.Exception ex)
        {
            return PaymentApiMapper.ErrorResult(ex);
        }
    }
}
