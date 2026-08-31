using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentWorkflowService _workflow;

    public OrdersController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), 201)]
    public async Task<ActionResult<CreateOrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _workflow.PlaceOrderAsync(BuyerId, request, cancellationToken);
        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderResponse), 200)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.PayAsync(BuyerId, orderId, request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), 200)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), 200)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), 200)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _workflow.RefundAsync(BuyerId, orderId, request, cancellationToken));

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), 200)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _workflow.GetMyOrdersAsync(BuyerId, cancellationToken));

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ??
        throw new UnauthorizedAccessException("The bearer token has no name claim.");
}
