using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: refunds a captured payment, in full or in part. The caller-supplied
/// idempotency key guarantees a repeated request never refunds twice; distinct keys
/// allow multiple partial refunds up to the captured amount.
/// </summary>
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
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Refunds a fulfilled order",
        Description = "Operator-only. Refunds the captured payment in full (no amount) or in part (amount).",
        OperationId = "orders.refund",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<RefundOrderResponse>> HandleAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var orderId = int.Parse((string)RouteData.Values["orderId"]!);
        try
        {
            var refund = await _orderPaymentService.RefundOrderAsync(
                orderId,
                request.IdempotencyKey,
                request.Amount,
                request.NoteToPayer,
                cancellationToken);

            return new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                OrderId = orderId,
                Amount = refund.Amount,
                Status = refund.Status
            };
        }
        catch (OrderNotFoundException)
        {
            return NotFound();
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating the request under the same key
    /// returns the original refund instead of refunding again.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Partial refund amount; omit to refund the remaining captured amount.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    [MaxLength(255)]
    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
