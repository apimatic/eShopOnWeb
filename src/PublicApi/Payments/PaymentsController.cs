using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentsController(PaymentService payments) => _payments = payments;

    [HttpPost("orders")]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceOrder(
        PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.PlaceOrderAsync(BuyerId, request, cancellationToken);
        return Created($"/api/orders/{result.OrderId}", result);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public Task<AuthorizationResponse> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        _payments.PayAsync(BuyerId, orderId, request, cancellationToken);

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<CaptureResponse> Fulfil(int orderId, CancellationToken cancellationToken) =>
        _payments.FulfilAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<CancellationResponse> Cancel(int orderId, CancellationToken cancellationToken) =>
        _payments.CancelAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/refunds")]
    public Task<RefundResponse> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) =>
        _payments.RefundAsync(BuyerId, orderId, request, cancellationToken);

    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderSummaryResponse>> MyOrders(CancellationToken cancellationToken) =>
        _payments.MyOrdersAsync(BuyerId, cancellationToken);

    [HttpPost("payment-methods")]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.SavePaymentMethodAsync(BuyerId, request, cancellationToken);
        return Created($"/api/payment-methods/{result.PaymentMethodId}", result);
    }

    [HttpGet("payment-methods")]
    public Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethods(CancellationToken cancellationToken) =>
        _payments.PaymentMethodsAsync(BuyerId, cancellationToken);

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationResponse> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        _payments.ReconcileAsync(from, to, cancellationToken);

    private string BuyerId => User.Identity?.Name
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized,
            "The bearer token does not contain a shopper identity.");
}
