using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public OrdersController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost("api/orders")]
    public async Task<ActionResult<CreateOrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        var response = await _payments.CreateOrderAsync(buyerId, request, cancellationToken);
        return Created($"api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<PayOrderResponse>> Pay(
        int orderId,
        [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        return Ok(await _payments.PayAsync(buyerId, orderId, request, cancellationToken));
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<FulfilOrderResponse>> Fulfil(
        int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<CancelOrderResponse>> Cancel(
        int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        [FromBody] RefundRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
            return Unauthorized();

        return Ok(await _payments.RefundAsync(buyerId, orderId, request, cancellationToken));
    }
}
