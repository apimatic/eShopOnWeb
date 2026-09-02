using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part. The caller-supplied idempotency key
/// guarantees a repeated request under the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundOrderRequest, HttpContext>
{
    private readonly IOrderPaymentService _paymentService;

    public RefundOrderEndpoint(IOrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, request, httpContext);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        try
        {
            var refund = await _paymentService.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, request.Note);

            return Results.Ok(new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                OrderId = orderId,
                Amount = refund.Amount,
                Status = refund.Status,
                IdempotencyKey = refund.IdempotencyKey
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating the request with the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
