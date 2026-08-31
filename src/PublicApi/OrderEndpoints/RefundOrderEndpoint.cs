using System;
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
/// Refunds a captured payment, in full (amount omitted) or in part. Repeating a
/// request under the same idempotency key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var (payment, refund, alreadyExisted) = await orderPaymentService.RefundOrderAsync(
            request.BuyerId!, request.OrderId, request.Amount, request.IdempotencyKey);

        response.RefundId = refund.Id;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.Status = refund.Status;
        response.Amount = refund.Amount;
        response.Currency = payment.Currency;
        response.TotalRefunded = payment.TotalRefunded;
        response.RemainingRefundable = payment.RemainingRefundable;
        response.AlreadyExisted = alreadyExisted;

        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }

    /// <summary>Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public bool AlreadyExisted { get; set; }
}
