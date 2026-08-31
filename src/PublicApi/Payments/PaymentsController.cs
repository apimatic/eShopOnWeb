using System;
using System.Collections.Generic;
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
    private readonly PaymentWorkflowService _workflow;

    public PaymentsController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpPost("orders")]
    [ProducesResponseType<PlaceOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(
        [FromBody] PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.PlaceOrderAsync(CallerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(
        int orderId,
        [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PayAsync(CallerId(), orderId, request, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        [FromBody] RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.RefundAsync(CallerId(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("my-orders")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _workflow.MyOrdersAsync(CallerId(), cancellationToken));

    [HttpGet("reconciliation")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<ReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.ReconcileAsync(from, to, cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType<PaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        [FromBody] SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.SavePaymentMethodAsync(CallerId(), request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType<IReadOnlyList<PaymentMethodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(CancellationToken cancellationToken) =>
        Ok(await _workflow.PaymentMethodsAsync(CallerId(), cancellationToken));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(CallerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string CallerId() =>
        User.Identity?.Name ?? throw new PaymentApiException("Authentication is required.", System.Net.HttpStatusCode.Unauthorized);
}
