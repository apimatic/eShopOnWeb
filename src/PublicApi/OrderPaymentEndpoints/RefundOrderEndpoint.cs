using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class RefundOrderPayload
{
    /// <summary>Amount to refund; omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never refunds twice;
    /// two distinct keys are two legitimate partial refunds.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }

    [FromBody]
    public RefundOrderPayload Refund { get; set; } = new();
}

public class RefundOrderResponse
{
    /// <summary>Top-level identifier of the refund created.</summary>
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a captured order in full or in part. Shopper-scoped: the caller can only refund their own
/// order, and a partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RefundOrderEndpoint : EndpointBaseAsync
    .WithRequest<RefundOrderRequest>
    .WithActionResult<RefundOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    [HttpPost("api/orders/{orderId}/refunds")]
    [SwaggerOperation(
        Summary = "Refunds a captured order (full or partial)",
        Description = "Refunds the captured payment. Idempotent on the caller-supplied key.",
        OperationId = "orders.refund",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<RefundOrderResponse>> HandleAsync(
        RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var payload = request.Refund ?? new RefundOrderPayload();
        if (string.IsNullOrWhiteSpace(payload.IdempotencyKey))
        {
            throw new PaymentException("A refund requires a non-empty idempotencyKey.");
        }

        var buyerId = User.GetBuyerId();
        var refund = await _orderPaymentService.RefundOrderAsync(
            buyerId, request.OrderId, payload.Amount, payload.IdempotencyKey, cancellationToken);

        var orders = await _orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);
        var order = orders.First(o => o.Id == request.OrderId);

        return Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderStatus = order.Status.ToString()
        });
    }
}
