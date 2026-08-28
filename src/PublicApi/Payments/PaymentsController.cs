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
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly CommercePaymentService _service;

    public PaymentsController(CommercePaymentService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.CreateOrderAsync(BuyerId, request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderPaymentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderPaymentResponse>> Pay(
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _service.PayAsync(orderId, BuyerId, request, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderPaymentResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RefundAsync(orderId, BuyerId, request, cancellationToken);
        return Created($"/api/orders/{orderId}", response);
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<MyOrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetMyOrdersAsync(BuyerId, cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.SavePaymentMethodAsync(BuyerId, request, cancellationToken);
        return Created($"/api/payment-methods/{response.PaymentMethodId}", response);
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(BuyerId, cancellationToken));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(paymentMethodId, BuyerId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentApiException(StatusCodes.Status401Unauthorized, "USER_IDENTITY_REQUIRED", "The bearer token has no shopper identity.");
}
