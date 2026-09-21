using System;
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

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit for a full refund of what remains.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; may also be sent as the <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalRefundId { get; set; }
}

/// <summary>
/// Refunds a captured payment (in full or in part) for the caller's own order, under a caller-supplied
/// idempotency key. A repeat under the same key never refunds twice; distinct keys are distinct refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.CurrentBuyerId(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var key = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    key = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "A refund requires an idempotency key (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                request.OrderId = orderId;
                request.BuyerId = buyerId;
                request.IdempotencyKey = key;
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service)
    {
        var refund = await service.RefundOrderAsync(request.BuyerId, request.OrderId, request.Amount,
            request.IdempotencyKey!, request.Ct);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            PayPalRefundId = refund.PayPalRefundId
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
