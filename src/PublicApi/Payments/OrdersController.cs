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
public sealed class OrdersController : ControllerBase
{
    private readonly PaymentService _payments;
    private readonly ReconciliationService _reconciliation;

    public OrdersController(PaymentService payments, ReconciliationService reconciliation)
    {
        _payments = payments;
        _reconciliation = reconciliation;
    }

    [HttpPost("orders")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDto>> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _payments.PlaceOrderAsync(BuyerId, request, cancellationToken);
        return Created($"/api/orders/{order.OrderId}", order);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public Task<PaymentDto> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken) =>
        _payments.PayAsync(BuyerId, orderId, request, cancellationToken);

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<PaymentDto> Fulfil(int orderId, CancellationToken cancellationToken) =>
        _payments.FulfilAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<OrderDto> Cancel(int orderId, CancellationToken cancellationToken) =>
        _payments.CancelAsync(orderId, cancellationToken);

    [HttpPost("orders/{orderId:int}/refunds")]
    public Task<RefundDto> Refund(int orderId, RefundOrderRequest request, CancellationToken cancellationToken) =>
        _payments.RefundAsync(BuyerId, orderId, request, cancellationToken);

    [HttpGet("my-orders")]
    public Task<IReadOnlyList<OrderDto>> MyOrders(CancellationToken cancellationToken) =>
        _payments.GetMyOrdersAsync(BuyerId, cancellationToken);

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationReport> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        _reconciliation.BuildAsync(from, to, cancellationToken);

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentApiException(401, "UNAUTHENTICATED", "The token does not identify a shopper.");
}
