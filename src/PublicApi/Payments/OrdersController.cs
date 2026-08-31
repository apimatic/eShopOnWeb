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
    [ProducesResponseType(typeof(OrderCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.PlaceOrderAsync(BuyerId(), request, cancellationToken);
        return Created($"/api/orders/{result.OrderId}", result);
    }

    [HttpPost("{orderId:int}/pay")]
    public Task<OrderActionResponse> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        _payments.PayAsync(orderId, BuyerId(), request, cancellationToken);

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<OrderActionResponse> Fulfil(int orderId, CancellationToken cancellationToken) =>
        _payments.FulfilAsync(orderId, cancellationToken);

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<OrderActionResponse> Cancel(int orderId, CancellationToken cancellationToken) =>
        _payments.CancelAsync(orderId, cancellationToken);

    [HttpPost("{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundCreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundCreatedResponse>> Refund(int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.RefundAsync(orderId, BuyerId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("/api/my-orders")]
    public Task<IReadOnlyList<MyOrderResponse>> MyOrders(CancellationToken cancellationToken) =>
        _payments.MyOrdersAsync(BuyerId(), cancellationToken);

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new UnauthorizedAccessException("The bearer token does not contain a name claim.");
}
