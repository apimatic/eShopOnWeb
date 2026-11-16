using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public OrdersController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost]
    public async Task<ActionResult<OrderSummary>> CreateOrder([FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.CreateOrderAsync(BuyerId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{orderId:int}/pay")]
    public async Task<ActionResult<PaymentSummary>> Pay(int orderId, [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.PayOrderAsync(BuyerId(), orderId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<PaymentSummary>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var result = await _payments.FulfilOrderAsync(orderId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<PaymentSummary>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var result = await _payments.CancelOrderAsync(orderId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{orderId:int}/refunds")]
    public async Task<ActionResult<RefundSummary>> Refund(int orderId, [FromBody] RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RefundOrderAsync(BuyerId(), orderId, request, cancellationToken);
        return Ok(result);
    }

    private string BuyerId()
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The request is not authenticated.");
        }

        return buyerId;
    }
}
