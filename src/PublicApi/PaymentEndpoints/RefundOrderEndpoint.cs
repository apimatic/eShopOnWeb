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

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RefundStatus { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a captured payment, in full or in part. Shopper-scoped (acts only on the caller's own order).
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service)
    {
        var ctx = _http.HttpContext!;

        // The idempotency key may come in the body or an Idempotency-Key header.
        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) && ctx.Request.Headers.TryGetValue("Idempotency-Key", out var headerVal))
            key = headerVal.ToString();
        if (string.IsNullOrWhiteSpace(key))
            throw new PaymentValidationException("A refund idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header).");

        var outcome = await service.RefundAsync(ctx.User.BuyerId(), request.OrderId, request.Amount, key, ctx.RequestAborted);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{outcome.RefundId}", new RefundOrderResponse
        {
            RefundId = outcome.RefundId,
            PayPalRefundId = outcome.PayPalRefundId,
            Amount = outcome.Amount,
            RefundStatus = outcome.RefundStatus,
            TotalRefunded = outcome.TotalRefunded,
            PaymentStatus = outcome.PaymentStatus
        });
    }
}
