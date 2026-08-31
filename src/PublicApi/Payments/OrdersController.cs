using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    private readonly CommercePaymentService _service;
    private readonly PayPalOptions _options;

    public OrdersController(CommercePaymentService service, IOptions<PayPalOptions> options)
    {
        _service = service;
        _options = options.Value;
    }

    [HttpPost("api/orders")]
    [ProducesResponseType<CreateOrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> PlaceOrder(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _service.PlaceOrderAsync(Caller, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total(), order.Payment!.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await _service.PayAsync(Caller, orderId, request, cancellationToken);
        return Ok(CommercePaymentService.ToResponse(order, _options.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var order = await _service.FulfilAsync(orderId, cancellationToken);
        return Ok(CommercePaymentService.ToResponse(order, _options.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _service.CancelAsync(orderId, cancellationToken);
        return Ok(CommercePaymentService.ToResponse(order, _options.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var refund = await _service.RefundAsync(Caller, orderId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new RefundResponse(refund.Id, refund.PayPalRefundId, refund.PayPalStatus, refund.Amount, refund.Currency));
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType<IReadOnlyList<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _service.GetMyOrdersAsync(Caller, cancellationToken);
        return Ok(orders.Select(o => CommercePaymentService.ToResponse(o, _options.Currency)).ToList());
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType<ReconciliationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
        => Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string Caller => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The bearer token has no name claim.");
}
