using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Return against a fulfilled order — a full or partial refund keyed by a caller-supplied idempotency key.</summary>
public class RefundRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refund the caller's own captured order, in full or in part. A partly-refunded order never becomes
/// refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, IOrderPaymentService orderPaymentService) =>
                await HandleAsync(orderId, request, orderPaymentService))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundRequest request,
        IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey.");
        }

        var order = await orderPaymentService.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey);
        var payment = order.Payment!;
        var refund = payment.FindRefundByKey(request.IdempotencyKey)!;

        var response = new RefundResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status,
            TotalRefunded = payment.TotalRefunded(),
            RemainingRefundable = payment.RemainingRefundable(),
            OrderStatus = order.Status.ToString()
        };

        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}
