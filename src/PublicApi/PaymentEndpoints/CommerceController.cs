using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class CommerceController : ControllerBase
{
    private readonly CommerceService _service;

    public CommerceController(CommerceService service) => _service = service;

    [HttpPost("/api/orders")]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _service.PlaceOrderAsync(UserName(), request, cancellationToken);
        return Created($"/api/orders/{order.OrderId}", new PlaceOrderResponse(order.OrderId, order));
    }

    [HttpPost("/api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => Ok(await _service.PayAsync(UserName(), orderId, request, cancellationToken));

    [HttpPost("/api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("/api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("/api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RefundAsync(UserName(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{result.RefundId}", result);
    }

    [HttpGet("/api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetMyOrdersAsync(UserName(), cancellationToken));

    [HttpPost("/api/payment-methods")]
    public async Task<ActionResult<SavePaymentMethodResponse>> SavePaymentMethod(CardRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _service.SavePaymentMethodAsync(UserName(), request, cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}",
            new SavePaymentMethodResponse(method.PaymentMethodId, method));
    }

    [HttpGet("/api/payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        Ok(await _service.GetPaymentMethodsAsync(UserName(), cancellationToken));

    [HttpDelete("/api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(UserName(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("/api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string UserName() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new CommerceException(401, "Authentication required", "The bearer token has no user identity.");
}
