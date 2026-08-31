using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersPaymentController : ControllerBase
{
    private readonly CommerceService _commerce;
    public OrdersPaymentController(CommerceService commerce) => _commerce = commerce;

    [HttpPost("api/orders")]
    [ProducesResponseType<PlaceOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        var result = await _commerce.PlaceOrderAsync(UserName(), request, ct);
        return Created($"/api/orders/{result.OrderId}", result);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public Task<PaymentResponse> Pay(int orderId, PayOrderRequest request, CancellationToken ct) =>
        _commerce.PayAsync(UserName(), orderId, request, ct);

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<PaymentResponse> Fulfil(int orderId, CancellationToken ct) =>
        _commerce.FulfilAsync(orderId, ct);

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<PaymentResponse> Cancel(int orderId, CancellationToken ct) =>
        _commerce.CancelAsync(orderId, ct);

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundRequestDto request, CancellationToken ct)
    {
        var result = await _commerce.RefundAsync(UserName(), orderId, request, ct);
        return Created($"/api/orders/{orderId}/refunds/{result.RefundId}", result);
    }

    [HttpGet("api/my-orders")]
    public Task<IReadOnlyList<MyOrderResponse>> MyOrders(CancellationToken ct) =>
        _commerce.MyOrdersAsync(UserName(), ct);

    private string UserName() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "identity_missing",
            "The bearer token does not contain a shopper identity.");
}

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentMethodsController : ControllerBase
{
    private readonly CommerceService _commerce;
    public PaymentMethodsController(CommerceService commerce) => _commerce = commerce;

    [HttpPost("api/payment-methods")]
    [ProducesResponseType<SavePaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SavePaymentMethodResponse>> Save(SavePaymentMethodRequest request, CancellationToken ct)
    {
        var result = await _commerce.SavePaymentMethodAsync(UserName(), request, ct);
        return Created($"/api/payment-methods/{result.PaymentMethodId}", result);
    }

    [HttpGet("api/payment-methods")]
    public Task<IReadOnlyList<PaymentMethodResponse>> List(CancellationToken ct) =>
        _commerce.PaymentMethodsAsync(UserName(), ct);

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> Delete(int paymentMethodId, CancellationToken ct)
    {
        await _commerce.DeletePaymentMethodAsync(UserName(), paymentMethodId, ct);
        return NoContent();
    }

    private string UserName() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "identity_missing",
            "The bearer token does not contain a shopper identity.");
}

[ApiController]
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReconciliationController : ControllerBase
{
    private readonly CommerceService _commerce;
    public ReconciliationController(CommerceService commerce) => _commerce = commerce;

    [HttpGet("api/reconciliation")]
    public Task<ReconciliationResponse> Get([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken ct) => _commerce.ReconcileAsync(from, to, ct);
}
