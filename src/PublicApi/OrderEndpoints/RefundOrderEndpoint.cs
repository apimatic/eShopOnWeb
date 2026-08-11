using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns money after fulfilment, fully or in part. The
/// caller supplies an idempotency key so repeating a request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RefundOrderRequest request, IPaymentService service, HttpContext ctx) =>
                await HandleAsync(request, service, ctx))
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);
        var orderId = PaymentMapper.GetRouteInt(ctx, "orderId");

        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotency key is required.");
        }

        var (order, refundId) = await service.RefundOrderAsync(
            buyerId, orderId, request.Amount, request.IdempotencyKey, ctx.RequestAborted);

        return Results.Ok(new RefundOrderResponse(refundId, orderId, PaymentMapper.ToPaymentDto(order.Payment)));
    }
}

public class RefundOrderRequest
{
    /// <summary>The amount to refund. Omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response carrying the refund's identifier as a top-level field.</summary>
public record RefundOrderResponse(string RefundId, int OrderId, PaymentDto? Payment);
