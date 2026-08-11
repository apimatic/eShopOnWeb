using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Returns a fulfilled order's money, in full or in part, guarded by a caller-supplied idempotency key.
/// The caller must own the order (an administrator may refund any order). Returns the <c>refundId</c>.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext context) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, context);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext context)
    {
        var response = new RefundOrderResponse(request.CorrelationId());
        var paymentService = context.RequestServices.GetRequiredService<IPaymentService>();

        var refund = await paymentService.RefundAsync(
            request.OrderId,
            request.Amount,
            request.IdempotencyKey,
            context.User.BuyerId(),
            context.User.IsAdministrator());

        response.RefundId = refund.PayPalRefundId;
        response.OrderId = request.OrderId;
        response.Status = refund.Status;
        response.AmountRefunded = refund.Amount;

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the created refund.</summary>
    public string RefundId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal AmountRefunded { get; set; }
}
