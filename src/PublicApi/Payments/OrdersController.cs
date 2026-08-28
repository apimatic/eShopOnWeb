using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;
    public OrdersController(PaymentService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _payments.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{order.Id}", new { orderId = order.Id, order = PaymentService.View(order) });
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<IActionResult> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _payments.PayAsync(orderId, BuyerId(), request, cancellationToken);
        return Ok(PaymentService.View(order));
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.FulfilAsync(orderId, cancellationToken);
        return Ok(PaymentService.View(order));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.CancelAsync(orderId, cancellationToken);
        return Ok(PaymentService.View(order));
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(orderId, BuyerId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new
        {
            refundId = refund.PayPalRefundId,
            refund.Status,
            refund.Amount,
            refund.CreatedAt
        });
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderView>>> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _payments.MyOrdersAsync(BuyerId(), cancellationToken);
        return Ok(orders.Select(PaymentService.View).ToList());
    }

    private string BuyerId() => User.Identity?.Name
        ?? throw new PaymentApiException(401, "UNAUTHENTICATED", "A valid bearer token is required.");
}
