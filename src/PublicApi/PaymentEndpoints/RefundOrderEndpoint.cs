using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured payment for the caller's own order, in full or in part. The caller supplies an idempotency
/// key so repeating the request under the same key does not refund twice; two distinct keys are two partial refunds.
/// Returns the new refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IPaymentService _payments;

    public RefundOrderEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });
        }

        var refund = await _payments.RefundAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);
        var payment = await _payments.GetOwnedPaymentAsync(request.OrderId, request.BuyerId);

        var refundDto = new RefundDto(refund.PayPalRefundId, refund.Amount, refund.Status, refund.IdempotencyKey, refund.CreatedAt);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}",
            new RefundResponse(refund.PayPalRefundId, refundDto, payment.ToDto()));
    }
}
