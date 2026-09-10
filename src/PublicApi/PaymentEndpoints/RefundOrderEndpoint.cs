using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a captured order, in full or in part. Shopper-scoped.
/// The caller supplies an idempotency key (body field or <c>Idempotency-Key</c> header); repeating under
/// the same key does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                IPaymentService paymentService,
                ClaimsPrincipal user,
                HttpContext http) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey)
                    && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                    idempotencyKey = header.ToString();

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest(new { message = "A refund requires an idempotency key (body 'idempotencyKey' or 'Idempotency-Key' header)." });

                var view = await paymentService.RefundAsync(
                    orderId, buyerId, request.Amount, idempotencyKey!, http.RequestAborted);

                return Results.Ok(view);
            })
            .Produces<RefundView>()
            .WithTags("PaymentEndpoints");
    }
}
