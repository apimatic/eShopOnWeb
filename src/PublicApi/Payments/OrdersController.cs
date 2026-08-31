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
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;
    public OrdersController(PaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.CreateOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{order.OrderId}", order);
    }

    [HttpPost("{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _payments.PayAsync(orderId, BuyerId(), request, cancellationToken));

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken) => Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken) => Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(orderId, BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{refund.RefundId}", refund);
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new PaymentOperationException(401, "The bearer token has no shopper identity.");
}

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MyOrdersController : ControllerBase
{
    private readonly PaymentService _payments;
    public MyOrdersController(PaymentService payments) => _payments = payments;

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyCollection<OrderResponse>>> Get(CancellationToken cancellationToken)
    {
        var buyerId = User.FindFirstValue(ClaimTypes.Name)
            ?? throw new PaymentOperationException(401, "The bearer token has no shopper identity.");
        return Ok(await _payments.MyOrdersAsync(buyerId, cancellationToken));
    }
}
