using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class PaymentsController : ControllerBase
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly PaymentApplicationService _service;

    public PaymentsController(PaymentApplicationService service)
    {
        _service = service;
    }

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.PlaceOrderAsync(CallerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(PayOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayOrderResponse>> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.PayAsync(CallerId(), orderId, request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(FulfilOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FulfilOrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CancelOrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId, RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RefundAsync(CallerId(), orderId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetOrdersAsync(CallerId(), cancellationToken));

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.SavePaymentMethodAsync(CallerId(), request.ToCardDetails(), cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("api/payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(CallerId(), cancellationToken));

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(CallerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue || !to.HasValue)
        {
            ModelState.AddModelError("range", "Both from and to are required ISO-8601 date-times with an offset.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _service.ReconcileAsync(from.Value, to.Value, cancellationToken));
    }

    private string CallerId() => User.Identity?.Name ?? throw new UnauthorizedAccessException("The token has no caller identity.");
}
