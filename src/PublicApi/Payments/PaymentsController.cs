using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class PaymentsController : ControllerBase
{
    private readonly CommercePaymentService _service;
    public PaymentsController(CommercePaymentService service) { _service = service; }
    private string Shopper => User.Identity?.Name ?? throw new UnauthorizedAccessException("The token has no shopper identity.");

    [HttpPost("api/orders")]
    public async Task<ActionResult<object>> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    { var response = await _service.PlaceOrderAsync(Shopper, request, ct); var id = response.GetType().GetProperty("orderId")!.GetValue(response); return Created($"/api/orders/{id}", response); }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<object>> Pay(int orderId, PayOrderRequest request, CancellationToken ct) => Ok(await _service.PayAsync(Shopper, orderId, request, ct));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<object>> Fulfil(int orderId, CancellationToken ct) => Ok(await _service.FulfilAsync(orderId, ct));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<object>> Cancel(int orderId, CancellationToken ct) => Ok(await _service.CancelAsync(orderId, ct));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<object>> Refund(int orderId, RefundOrderRequest request, CancellationToken ct) => Ok(await _service.RefundAsync(Shopper, orderId, request, ct));

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<object>> MyOrders(CancellationToken ct) => Ok(await _service.MyOrdersAsync(Shopper, ct));

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<object>> SavePaymentMethod(SavePaymentMethodRequest request, CancellationToken ct)
    { var response = await _service.SavePaymentMethodAsync(Shopper, request, ct); return Created("/api/payment-methods", response); }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<object>> PaymentMethods(CancellationToken ct) => Ok(await _service.ListPaymentMethodsAsync(Shopper, ct));

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken ct)
    { await _service.DeletePaymentMethodAsync(Shopper, paymentMethodId, ct); return NoContent(); }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<object>> Reconciliation([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct) => Ok(await _service.ReconcileAsync(from, to, ct));
}
