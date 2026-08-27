using System;
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

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full or in part.
/// Repeating the request under the same idempotency key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await orderPaymentService.RefundOrderAsync(
            user.GetBuyerId(), request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);

        var payment = await orderPaymentService.GetPaymentForOrderAsync(request.OrderId);

        response.RefundId = refund.Id;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.OrderId = request.OrderId;
        response.Amount = refund.Amount;
        response.Currency = refund.Currency;
        response.Status = refund.Status;
        response.TotalRefunded = payment?.TotalRefunded ?? refund.Amount;
        response.RefundableRemaining = payment?.RefundableAmount ?? 0m;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key making the refund idempotent.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

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
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}
