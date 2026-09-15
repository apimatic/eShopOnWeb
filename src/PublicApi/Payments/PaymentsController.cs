using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentApplicationService _payments;

    public PaymentsController(PaymentApplicationService payments) => _payments = payments;

    [HttpPost("orders")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await _payments.PlaceOrderAsync(OwnerId(), request, ct);
        return Created($"/api/orders/{order.OrderId}", new { orderId = order.OrderId, order });
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<IActionResult> Pay(int orderId, PayOrderRequest request, CancellationToken ct) =>
        Ok(await _payments.PayAsync(OwnerId(), orderId, request, ct));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Fulfil(int orderId, CancellationToken ct) =>
        Ok(await _payments.FulfilAsync(orderId, ct));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken ct) =>
        Ok(await _payments.CancelAsync(orderId, ct));

    [HttpPost("orders/{orderId:int}/refunds")]
    public async Task<IActionResult> Refund(int orderId, CreateRefundRequest request, CancellationToken ct)
    {
        var refund = await _payments.RefundAsync(OwnerId(), orderId, request, ct);
        return Ok(new { refundId = refund.RefundId, refund });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken ct) =>
        Ok(await _payments.MyOrdersAsync(OwnerId(), ct));

    [HttpPost("payment-methods")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> SavePaymentMethod(SavePaymentMethodRequest request, CancellationToken ct)
    {
        var method = await _payments.SavePaymentMethodAsync(OwnerId(), request, ct);
        return Created($"/api/payment-methods/{method.PaymentMethodId}",
            new { paymentMethodId = method.PaymentMethodId, paymentMethod = method });
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods(CancellationToken ct) =>
        Ok(await _payments.PaymentMethodsAsync(OwnerId(), ct));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken ct)
    {
        await _payments.DeletePaymentMethodAsync(OwnerId(), paymentMethodId, ct);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken ct) =>
        Ok(await _payments.ReconcileAsync(from, to, ct));

    private string OwnerId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentApplicationException(401, "Unauthenticated", "The access token has no shopper identity.");
}
