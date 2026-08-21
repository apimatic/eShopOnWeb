using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;

    /// <summary>Amount to refund; null refunds the full refundable remaining amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>The new refund's identifier (top-level, so the flow can be driven end to end).</summary>
    public int RefundId { get; set; }

    public string? PayPalRefundId { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Returns a captured payment, in full or in part. A partly-refunded order never becomes refundable
/// beyond what was captured. Shopper-scoped — acts only on the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.CallerId = user.GetUserName();
                return await HandleAsync(request, service, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required for a refund." });
        }

        var result = await service.RefundAsync(request.OrderId, request.Amount, request.IdempotencyKey, request.CallerId, ct);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.RefundId,
            PayPalRefundId = result.PayPalRefundId,
            Status = result.Status,
            Amount = result.Amount,
            CurrencyCode = result.CurrencyCode,
            Payment = PaymentDtoMapper.ToDto(result.Payment)
        });
    }
}
