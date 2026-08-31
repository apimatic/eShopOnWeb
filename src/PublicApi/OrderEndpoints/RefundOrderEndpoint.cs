using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns money after fulfilment: refunds the captured payment in full
/// (amount omitted) or in part. Idempotent per idempotencyKey — repeating the
/// same key never refunds twice; distinct keys allow multiple partial refunds,
/// never beyond the captured amount.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
                {
                    return Results.BadRequest(new { message = "idempotencyKey is required (max 80 characters)." });
                }
                if (request.Amount is <= 0)
                {
                    return Results.BadRequest(new { message = "amount must be positive when provided." });
                }

                var refund = await paymentService.RefundOrderAsync(buyerId, orderId, request.Amount,
                    request.IdempotencyKey, request.NoteToPayer, ct);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = refund.PayPalRefundId,
                    OrderId = orderId,
                    Status = refund.Status,
                    Amount = refund.Amount,
                    IdempotencyKey = refund.IdempotencyKey,
                    CreatedAt = refund.CreatedAt
                };
                return Results.Ok(response);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
