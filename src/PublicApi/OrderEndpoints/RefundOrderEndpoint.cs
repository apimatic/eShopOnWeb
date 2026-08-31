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
/// Operator action: refunds a fulfilled order's captured payment, in full
/// (amount omitted) or in part. The idempotency key makes a repeated request
/// return the original refund instead of refunding twice; distinct keys allow
/// distinct partial refunds of the same capture.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { Message = "idempotencyKey is required." });
        }
        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.BadRequest(new { Message = "amount must be greater than zero." });
        }

        var refund = await orderPaymentService.RefundAsync(request.OrderId, request.Amount,
            request.IdempotencyKey, request.NoteToPayer);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Status = refund.Status.ToString(),
            Amount = refund.Amount,
            Currency = refund.Currency
        };
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }
    /// <summary>Partial amount; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }
    /// <summary>Caller-supplied key; repeating the request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
