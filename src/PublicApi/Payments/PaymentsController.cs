using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentApplicationService _payments;

    public PaymentsController(PaymentApplicationService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        var result = await _payments.PlaceOrderAsync(Shopper(), request, ct);
        return Created($"/api/orders/{result.OrderId}", result);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public Task<PayOrderResponse> Pay(int orderId, PayOrderRequest request, CancellationToken ct) =>
        _payments.PayAsync(Shopper(), orderId, request, ct);

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<FulfilOrderResponse>> Fulfil(int orderId, CancellationToken ct)
    {
        var response = await _payments.FulfilAsync(orderId, ct);
        return response.PaymentState == "CapturePending" ? Accepted(response) : Ok(response);
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<CancelOrderResponse> Cancel(int orderId, CancellationToken ct) =>
        _payments.CancelAsync(orderId, ct);

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken ct)
    {
        var response = await _payments.RefundAsync(Shopper(), orderId, request, ct);
        return StatusCode((int)HttpStatusCode.Created, response);
    }

    [HttpGet("api/my-orders")]
    public Task<IReadOnlyList<MyOrderResponse>> MyOrders(CancellationToken ct) =>
        _payments.MyOrdersAsync(Shopper(), ct);

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request,
        CancellationToken ct)
    {
        var response = await _payments.SavePaymentMethodAsync(Shopper(), request, ct);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    public Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethods(CancellationToken ct) =>
        _payments.ListPaymentMethodsAsync(Shopper(), ct);

    [HttpDelete("api/payment-methods/{paymentMethodId}")]
    public async Task<IActionResult> DeletePaymentMethod(string paymentMethodId, CancellationToken ct)
    {
        await _payments.DeletePaymentMethodAsync(Shopper(), paymentMethodId, ct);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationResponse> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken ct) => _payments.ReconcileAsync(from, to, ct);

    private string Shopper() => User.Identity?.Name ??
        throw new PaymentApiException(HttpStatusCode.Unauthorized, "An authenticated shopper identity is required.");
}
