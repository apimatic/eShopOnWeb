using System.Security.Claims;
using System.Threading;
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
/// Refunds a captured payment, in full or in part, under a caller-supplied idempotency key
/// (request body <c>idempotencyKey</c> or the <c>Idempotency-Key</c> header). Shopper-scoped.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpContext http, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name;
                request.Cancellation = ct;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }

                return await HandleAsync(request, service);
            })
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "A caller-supplied idempotencyKey is required for a refund." });
        }

        var refund = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey!, request.Cancellation);
        return Results.Created(
            $"/api/orders/{request.OrderId}/refunds/{refund.Id}",
            new { refundId = refund.PayPalRefundId, id = refund.Id, status = refund.Status, amount = refund.Amount });
    }
}
