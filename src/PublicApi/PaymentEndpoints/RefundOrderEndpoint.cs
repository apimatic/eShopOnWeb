using System.Security.Claims;
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

/// <summary>
/// Refunds a captured payment, in full or in part. Carries a caller-supplied idempotency key: repeating
/// a request under the same key never refunds twice, while two distinct partial refunds remain legitimate.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = BuyerIdentity.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for a refund.", 400);
        }

        var result = await paymentService.RefundOrderAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = result.RefundId,
            PayPalRefundId = result.PayPalRefundId,
            Status = result.Status,
            Amount = result.Amount,
            TotalRefunded = result.TotalRefunded,
            PaymentStatus = result.PaymentStatus
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{result.RefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal TotalRefunded { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
