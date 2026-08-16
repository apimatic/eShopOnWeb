using System;
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

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>The identifier of the refund.</summary>
    public int RefundId { get; set; }
    public RefundDto? Refund { get; set; }
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a captured payment, in full or in part.
/// Shopper-scoped; acts only on the caller's own order. Idempotent via the caller's key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        try
        {
            var refund = await paymentService.RefundAsync(request.OrderId, buyerId, request.Amount,
                request.IdempotencyKey, request.NoteToPayer);
            var payment = await paymentService.GetPaymentByOrderIdAsync(request.OrderId);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                Refund = new RefundDto
                {
                    Id = refund.Id,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    PayPalRefundId = refund.PayPalRefundId,
                    CreatedAt = refund.CreatedAt
                },
                Payment = payment is null ? null : PaymentMapper.ToStateDto(payment)
            };
            return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
        }
        catch (PaymentNotFoundException ex)
        {
            return PaymentResults.NotFound(ex);
        }
        catch (PaymentException ex)
        {
            return PaymentResults.FromException(ex);
        }
    }
}
