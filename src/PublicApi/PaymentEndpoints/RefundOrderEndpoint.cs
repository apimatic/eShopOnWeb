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
/// POST /api/orders/{orderId}/refunds — refunds the caller's own captured order, fully or partially, never
/// beyond the captured amount. Carries a caller-supplied idempotency key. Returns the new refund's
/// identifier as a top-level <c>refundId</c>.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundBody body, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new RefundRequest(orderId, body?.Amount, body?.IdempotencyKey ?? string.Empty), service, http))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundRequest request, IOrderPaymentService service, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        var buyerId = http.BuyerId();
        var outcome = await service.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey, http.RequestAborted);

        var response = new RefundResponse(
            outcome.Refund.PayPalRefundId, outcome.Refund.Status, outcome.Refund.Amount,
            outcome.Order.Id, outcome.Order.Status.ToString());
        return Results.Created($"api/orders/{request.OrderId}/refunds/{outcome.Refund.PayPalRefundId}", response);
    }
}
