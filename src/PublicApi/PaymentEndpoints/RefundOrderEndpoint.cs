using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Returns a fulfilled order's captured payment, in full or in part. Carries a caller-supplied
/// idempotency key: repeating it never refunds twice, while distinct keys are distinct refunds.
/// A partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentMapping.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? throw new ApplicationCore.Exceptions.PaymentValidationException("An idempotencyKey is required for a refund.")
            : request.IdempotencyKey!;

        var result = await paymentService.RefundOrderAsync(request.BuyerId, request.OrderId, request.Amount, key);

        response.RefundId = result.RefundId;
        response.PayPalRefundId = result.PayPalRefundId;
        response.Status = result.Status;
        response.Amount = result.Amount;
        response.CurrencyCode = result.CurrencyCode;
        response.RefundableRemaining = result.RefundableRemaining;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key, unique per distinct refund.</summary>
    public string? IdempotencyKey { get; set; }

    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal RefundableRemaining { get; set; }
}
