using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key
    /// returns the original refund instead of refunding again.
    /// </summary>
    [Required]
    [MaxLength(108)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
}

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full or in part.
/// A partly-refunded order can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await _paymentService.RefundOrderAsync(
                buyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);

            return Results.Ok(new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = result.Refund.Id,
                PayPalRefundId = result.Refund.PayPalRefundId,
                OrderId = request.OrderId,
                Amount = result.Refund.Amount,
                Currency = result.Refund.Currency,
                Status = result.Refund.Status,
                IdempotencyKey = result.Refund.IdempotencyKey,
                TotalRefunded = result.TotalRefunded,
                RefundableAmount = result.RefundableAmount
            });
        }
        catch (Exception ex) when (PaymentEndpointHelpers.TryMapException(ex) is { } result)
        {
            return result;
        }
    }
}
