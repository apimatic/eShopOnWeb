using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;

    public OrdersController(PaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), (int)HttpStatusCode.Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.CreateOrderAsync(Caller(), request, cancellationToken);
        var response = new CreateOrderResponse(order.OrderId, order);
        return Created($"/api/orders/{order.OrderId}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(PayOrderResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<PayOrderResponse>> Pay(
        int orderId,
        [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await _payments.PayAsync(Caller(), orderId, request, cancellationToken);
        return Ok(new PayOrderResponse(orderId, payment));
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(OrderResponse), (int)HttpStatusCode.Accepted)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.FulfilAsync(orderId, cancellationToken);
        if (string.Equals(order.Payment?.Status, "CapturePending", StringComparison.Ordinal))
        {
            return Accepted($"/api/orders/{orderId}/fulfil", order);
        }
        return Ok(order);
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        return Ok(await _payments.CancelAsync(orderId, cancellationToken));
    }

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), (int)HttpStatusCode.Created)]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        [FromBody] CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RefundAsync(Caller(), orderId, request, cancellationToken);
        var response = new RefundResponse(result.Refund.RefundId, orderId, result.Refund, result.Payment);
        return Created($"/api/orders/{orderId}/refunds/{result.Refund.RefundId}", response);
    }

    [HttpGet("/api/my-orders")]
    [ProducesResponseType(typeof(MyOrdersResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<MyOrdersResponse>> MyOrders(CancellationToken cancellationToken)
    {
        return Ok(new MyOrdersResponse(await _payments.GetMyOrdersAsync(Caller(), cancellationToken)));
    }

    private string Caller() => User.Identity?.Name ?? string.Empty;
}
