using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order's captured payment for the signed-in shopper, in full or in part, under a
/// caller-supplied idempotency key. Returns the new refund's id as a top-level <c>refundId</c>.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpContext http,
                CancellationToken ct, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.UserId = user.GetUserId();
                request.Cancellation = ct;
                // Idempotency key may come in the body or the Idempotency-Key header.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new InvalidPaymentOperationException(
                "A refund requires an idempotency key (body field 'idempotencyKey' or 'Idempotency-Key' header).");
        }

        var refund = await service.RefundAsync(request.UserId, request.OrderId, request.Amount,
            request.IdempotencyKey, request.Cancellation);

        var response = new RefundResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of what remains.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string UserId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

public class RefundResponse : BaseResponse
{
    public RefundResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
