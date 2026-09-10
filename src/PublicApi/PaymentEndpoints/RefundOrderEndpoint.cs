using System;
using System.Linq;
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

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the created refund (top-level).</summary>
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? PayPalRefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal RefundedToDate { get; set; }
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// Returns money after fulfilment: refunds the captured payment in full or in part. A partly
/// refunded order can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
             CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var refund = await service.RefundAsync(orderId, buyerId, request.Amount, request.IdempotencyKey, ct);

                // Re-read the payment so the caller sees the resulting totals/status.
                var summaries = await service.GetOrdersForBuyerAsync(buyerId, ct);
                var summary = summaries.FirstOrDefault(s => s.OrderId == orderId);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}",
                    new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = refund.Id,
                        OrderId = orderId,
                        Amount = refund.Amount,
                        PayPalRefundId = refund.PayPalRefundId,
                        Status = refund.Status,
                        PaymentStatus = summary?.PaymentStatus ?? string.Empty,
                        RefundedToDate = summary?.RefundedToDate ?? refund.Amount,
                        RefundableRemaining = summary is null ? 0m
                            : (summary.CapturedGross ?? 0m) - summary.RefundedToDate
                    });
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.Empty as IResult);
}
