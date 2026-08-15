using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key does not refund
    /// twice; two distinct partial refunds use two distinct keys. May also be supplied via the
    /// <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}

/// <summary>
/// Returns a fulfilled order's captured payment, in full or in part. Shopper-scoped: acts only on
/// the caller's own order. A partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService, IHttpContextAccessor httpContextAccessor)
    {
        _orderPaymentService = orderPaymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = httpContext.GetBuyerId();

        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            && httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
        {
            idempotencyKey = headerKey.ToString();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(
                "A refund requires a caller-supplied idempotency key (body 'idempotencyKey' or 'Idempotency-Key' header).");
        }

        var refund = await _orderPaymentService.RefundAsync(request.OrderId, buyerId, request.Amount, idempotencyKey!);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            Refund = new RefundDto
            {
                Id = refund.Id,
                RefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status,
                IdempotencyKey = refund.IdempotencyKey,
                CreatedAt = refund.CreatedAt
            }
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
