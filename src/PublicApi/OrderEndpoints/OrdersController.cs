using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class OrdersController : ControllerBase
{
    private readonly OrderPaymentService _orderPaymentService;

    public OrdersController(OrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders")]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _orderPaymentService.CreateOrderAsync(
            GetBuyerId(),
            request,
            cancellationToken);

        return Created($"/api/orders/{response.OrderId}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(
        int orderId,
        [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _orderPaymentService.PayAsync(
            GetBuyerId(),
            orderId,
            request,
            cancellationToken));
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(
        int orderId,
        CancellationToken cancellationToken)
    {
        return Ok(await _orderPaymentService.FulfilAsync(orderId, cancellationToken));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(
        int orderId,
        CancellationToken cancellationToken)
    {
        return Ok(await _orderPaymentService.CancelAsync(orderId, cancellationToken));
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundResponse>> Refund(
        int orderId,
        [FromBody] CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _orderPaymentService.RefundAsync(
            GetBuyerId(),
            orderId,
            request,
            cancellationToken);

        return Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<MyOrdersResponse>> MyOrders(CancellationToken cancellationToken)
    {
        return Ok(await _orderPaymentService.GetMyOrdersAsync(GetBuyerId(), cancellationToken));
    }

    private string GetBuyerId()
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new UnauthorizedAccessException("The bearer token does not contain a user identity.");
        }

        return buyerId;
    }
}

