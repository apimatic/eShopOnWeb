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
    [ProducesResponseType<CreateOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.CreateOrderAsync(ShopperId, request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType<OrderPaymentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderPaymentResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PayAsync(ShopperId, orderId, request, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderPaymentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderPaymentResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderPaymentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderPaymentResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.RefundAsync(ShopperId, orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("my-orders")]
    [ProducesResponseType<IReadOnlyList<MyOrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _workflow.GetMyOrdersAsync(ShopperId, cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType<PaymentMethodResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.SavePaymentMethodAsync(ShopperId, request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType<IReadOnlyList<PaymentMethodResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        Ok(await _workflow.GetPaymentMethodsAsync(ShopperId, cancellationToken));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(ShopperId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<ReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _workflow.ReconcileAsync(from, to, cancellationToken));

    private string ShopperId => User.Identity?.Name ??
        throw new PaymentWorkflowException(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "A valid shopper token is required.");
}
