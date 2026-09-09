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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A refund request: an optional partial amount (full when omitted) and an idempotency key.</summary>
public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }

    /// <summary>PayPal's own id for the refund.</summary>
    public string PayPalRefundId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public PaymentStateModel? Payment { get; set; }
}

/// <summary>
/// Returns money on the caller's own captured order, in full or in part. A partly-refunded order
/// never becomes refundable beyond what was captured; repeats under the same idempotency key are
/// safe, while distinct partial refunds are allowed.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException("An idempotencyKey is required for a refund.");
                }

                var refund = await paymentService.RefundAsync(buyerId, orderId, request.Amount,
                    request.IdempotencyKey, ct);
                var orders = await paymentService.GetMyOrdersAsync(buyerId, ct);
                var payment = orders.FirstOrDefault(o => o.Order.Id == orderId)?.Payment;

                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}",
                    new RefundOrderResponse(request.CorrelationId())
                    {
                        RefundId = refund.Id,
                        PayPalRefundId = refund.PayPalRefundId,
                        OrderId = orderId,
                        Payment = PaymentStateModel.From(payment)
                    });
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
