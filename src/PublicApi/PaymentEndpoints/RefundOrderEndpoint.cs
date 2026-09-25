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
/// Refunds a captured order — full or partial — under a caller-supplied idempotency key. Shopper-scoped
/// (the caller's own order). Repeating the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, http);
            })
            .Produces<RefundResponse>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotencyKey is required for a refund.");

        var (refundId, view) = await service.RefundAsync(request.OrderId, buyerId, request.Amount,
            request.IdempotencyKey, http.RequestAborted);
        return Results.Ok(new RefundResponse { RefundId = refundId, Order = view });
    }
}
