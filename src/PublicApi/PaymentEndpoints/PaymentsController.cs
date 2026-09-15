using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class PaymentsController : ControllerBase
{
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.CreateOrderAsync(ShopperId(), request, cancellationToken);
        return Created($"/api/orders/{result.OrderId}", result);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(PayOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PayOrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _payments.PayAsync(ShopperId(), orderId, request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(FulfilOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FulfilOrderResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<FulfilOrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken)
    {
        var result = await _payments.FulfilAsync(orderId, cancellationToken);
        return result.FulfilmentStatus == "Fulfilled" ? Ok(result) : Accepted(result);
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CancelOrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RefundAsync(ShopperId(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{result.RefundId}", result);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyCollection<OrderDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<OrderDto>>> MyOrders(
        CancellationToken cancellationToken) =>
        Ok(await _payments.GetMyOrdersAsync(ShopperId(), cancellationToken));

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.SavePaymentMethodAsync(ShopperId(), request, cancellationToken);
        return Created($"/api/payment-methods/{result.PaymentMethodId}", result);
    }

    [HttpGet("api/payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PaymentMethodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        Ok(await _payments.GetPaymentMethodsAsync(ShopperId(), cancellationToken));

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(ShopperId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = AdministratorRole, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _payments.ReconcileAsync(from, to, cancellationToken));

    private string ShopperId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentApiException(StatusCodes.Status401Unauthorized, "SHOPPER_ID_MISSING",
            "The bearer token does not contain a shopper identity.");
}
