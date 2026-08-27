using System.Security.Claims;
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
/// Refunds the captured payment of a fulfilled order, in full or in part.
/// Shopper-scoped: only the order's owner can refund it.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(request, user, orderId, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, int orderId,
        IOrderPaymentService paymentService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required.");
        }

        var refund = await paymentService.RefundPaymentAsync(buyerId, orderId, request.Amount,
            request.IdempotencyKey.Trim());

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            OrderId = orderId,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            IdempotencyKey = refund.IdempotencyKey
        };
        return Results.Ok(response);
    }
}
