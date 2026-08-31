using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersPaymentController : ControllerBase
{
    private readonly PaymentApplicationService _payments;

    public OrdersPaymentController(PaymentApplicationService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [SwaggerOperation(Summary = "Creates an order awaiting payment", Tags = new[] { "PaymentEndpoints" })]
    [ProducesResponseType(typeof(CreatePaidOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaidOrderResponse>> CreateOrder(CreatePaidOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.CreateOrderAsync(CallerId(), request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [SwaggerOperation(Summary = "Authorizes an order total on a card", Tags = new[] { "PaymentEndpoints" })]
    public Task<PayOrderResponse> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken) =>
        _payments.PayAsync(CallerId(), orderId, request, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(Summary = "Captures payment and fulfils an order", Tags = new[] { "PaymentEndpoints" })]
    public Task<FulfilOrderResponse> Fulfil(int orderId, CancellationToken cancellationToken) =>
        _payments.FulfilAsync(orderId, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(Summary = "Voids held funds and cancels an order", Tags = new[] { "PaymentEndpoints" })]
    public Task<CancelOrderResponse> Cancel(int orderId, CancellationToken cancellationToken) =>
        _payments.CancelAsync(orderId, cancellationToken);

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [SwaggerOperation(Summary = "Refunds all or part of a caller-owned fulfilled order", Tags = new[] { "PaymentEndpoints" })]
    [ProducesResponseType(typeof(RefundOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundOrderResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _payments.RefundAsync(CallerId(), orderId, request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    [SwaggerOperation(Summary = "Lists the caller's orders and payment state", Tags = new[] { "PaymentEndpoints" })]
    public Task<IReadOnlyList<OrderDto>> MyOrders(CancellationToken cancellationToken) =>
        _payments.GetMyOrdersAsync(CallerId(), cancellationToken);

    private string CallerId() => User.Identity?.Name ?? string.Empty;
}
