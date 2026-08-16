using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundResultDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured payment, in full or in part, under a caller
/// idempotency key. Never refundable beyond what was captured. Shopper-scoped to the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
                }

                var refund = await _paymentService.RefundAsync(orderId, buyerId, request.Amount, request.IdempotencyKey, ct);
                var result = new RefundResultDto
                {
                    RefundId = refund.Id,
                    PayPalRefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status
                };
                return Results.Created($"/api/orders/{orderId}/refunds/{refund.Id}", result);
            })
            .Produces<RefundResultDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
