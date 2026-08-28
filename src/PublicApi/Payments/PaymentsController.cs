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
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentWorkflowService _workflow;

    public PaymentsController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.PlaceOrderAsync(CurrentBuyer(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PayAsync(CurrentBuyer(), orderId, request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundCreatedResponse>> Refund(int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _workflow.RefundAsync(CurrentBuyer(), orderId, request,
            cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(
        CancellationToken cancellationToken) =>
        Ok(await _workflow.GetMyOrdersAsync(CurrentBuyer(), cancellationToken));

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var response = await _workflow.SavePaymentMethodAsync(CurrentBuyer(), request,
            cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        Ok(await _workflow.GetPaymentMethodsAsync(CurrentBuyer(), cancellationToken));

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(CurrentBuyer(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.ReconcileAsync(from, to, cancellationToken));

    private string CurrentBuyer() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new ApiProblemException(401, "Authentication required",
            "The bearer token does not contain a shopper identity.");
}
