using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderPaymentService _service;

    public OrdersController(OrderPaymentService service) => _service = service;

    [HttpPost("api/orders")]
    [ProducesResponseType<PlaceOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceOrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _service.PayAsync(BuyerId(), orderId, request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RefundAsync(BuyerId(), orderId, idempotencyKey, request,
            cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetMyOrdersAsync(BuyerId(), cancellationToken));

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<ReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(System.Net.HttpStatusCode.Unauthorized,
            "The bearer token does not identify a shopper.");
}
