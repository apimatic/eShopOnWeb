using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit for a full refund of what remains refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Idempotency key; may also be supplied via the <c>Idempotency-Key</c> request header.</summary>
    public string? IdempotencyKey { get; set; }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the refund that was created (or previously created under this key).</summary>
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Refunds a captured payment (after fulfilment), fully or partially. The caller-supplied idempotency
/// key makes a repeat safe; the total refunded can never exceed what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user,
             [FromHeader(Name = "Idempotency-Key")] string? headerKey, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                request.IdempotencyKey = string.IsNullOrWhiteSpace(headerKey) ? request.IdempotencyKey : headerKey;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentOperationException(
                "A refund requires an idempotency key (body 'idempotencyKey' or 'Idempotency-Key' header).", 400);

        var refund = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey!);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            CreatedAt = refund.CreatedAt
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}
