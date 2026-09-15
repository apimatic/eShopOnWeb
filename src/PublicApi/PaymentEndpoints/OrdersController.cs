using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly ICommercePaymentService _payments;

    public OrdersController(ICommercePaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> Create(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = User.Identity!.Name!;
        var lines = request.Items.Select(item => new OrderLineData(item.CatalogItemId, item.Quantity)).ToArray();
        var order = await _payments.CreateOrderAsync(buyerId, lines,
            request.ShippingAddress.ToAddress(), cancellationToken);
        var response = OrderResponse.From(order);
        return Created($"/api/orders/{order.Id}", response);
    }

    [HttpPost("{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.PayAsync(orderId, User.Identity!.Name!, request.Card?.ToData(),
            request.PaymentMethodId, cancellationToken);
        return Ok(OrderResponse.From(order));
    }

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.FulfilAsync(orderId, cancellationToken);
        return Ok(OrderResponse.From(order));
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.CancelAsync(orderId, cancellationToken);
        return Ok(OrderResponse.From(order));
    }

    [HttpPost("{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? headerIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = request.IdempotencyKey ?? headerIdempotencyKey ?? string.Empty;
        var refund = await _payments.RefundAsync(orderId, User.Identity!.Name!, request.Amount,
            key, cancellationToken);
        var response = new RefundResponse(refund.PayPalRefundId, refund.Amount, refund.Status, refund.CreatedAt);
        return Created($"/api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
    }

    [HttpGet("/api/my-orders")]
    public async Task<ActionResult<IReadOnlyCollection<OrderResponse>>> MyOrders(
        CancellationToken cancellationToken)
    {
        var orders = await _payments.GetOrdersAsync(User.Identity!.Name!, cancellationToken);
        return Ok(new { orders = orders.Select(OrderResponse.From).ToArray() });
    }
}
