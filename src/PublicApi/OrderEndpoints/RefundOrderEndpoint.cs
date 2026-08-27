using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
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
/// Operator: refunds a captured payment, in full (amount omitted) or in part. The
/// idempotency key guarantees a repeated request under the same key never refunds twice;
/// two distinct keys remain two legitimate partial refunds.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService, cancellationToken);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        return await HandleAsync(request, paymentService, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService, CancellationToken cancellationToken)
    {
        var refund = await paymentService.RefundOrderAsync(request.OrderId, request.Amount,
            request.IdempotencyKey, cancellationToken);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status
        });
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating the request under the same key never refunds twice.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
