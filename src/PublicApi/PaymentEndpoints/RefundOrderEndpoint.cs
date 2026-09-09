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

/// <summary>
/// POST /api/orders/{orderId}/refunds — return after fulfilment: refund the captured payment in
/// full or in part. Carries a caller-supplied idempotency key so a repeat never refunds twice,
/// while two distinct partial refunds remain legitimate. Never refundable beyond what was captured.
/// Shopper-scoped to the caller's own order. Returns the new <c>refundId</c> as a top-level field.
/// </summary>
public class RefundOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequestDto body, IPaymentService paymentService) =>
                await HandleAsync(new RefundOrderRequest
                {
                    OrderId = orderId,
                    Amount = body?.Amount,
                    IdempotencyKey = body?.IdempotencyKey ?? string.Empty
                }, paymentService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentConflictException("A refund requires a non-empty idempotencyKey.");
        }
        var result = await paymentService.RefundAsync(BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, RequestAborted);
        return Results.Ok(result);
    }
}
