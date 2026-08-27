using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns the
    /// original refund instead of refunding again; distinct keys allow multiple partial refunds.
    /// </summary>
    [Required]
    [MaxLength(108)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RefundStatus { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part.
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
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Refunds a fulfilled order",
        Description = "Refunds the captured payment in full (amount omitted) or in part. Repeating the same idempotency key never refunds twice.",
        OperationId = "orders.refund",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<RefundOrderResponse>> HandleAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return BadRequest("An idempotencyKey is required.");
        }

        var orderId = int.Parse(RouteData.Values["orderId"]!.ToString()!);
        var isAdmin = User.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var (order, refund) = await _orderPaymentService.RefundOrderAsync(
            buyerId, isAdmin, orderId, request.Amount, request.IdempotencyKey, cancellationToken);

        return new RefundOrderResponse
        {
            OrderId = order.Id,
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Currency = refund.CurrencyCode,
            RefundStatus = refund.Status,
            OrderStatus = order.Status.ToString(),
            TotalRefunded = order.Payment?.TotalRefunded ?? 0m,
            RemainingRefundable = order.Payment?.RefundableAmount ?? 0m
        };
    }
}
