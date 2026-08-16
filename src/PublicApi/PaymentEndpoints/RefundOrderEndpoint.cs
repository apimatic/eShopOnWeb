using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured payment for the caller's own order, in full or in part. Carries an idempotency
/// key so a repeat under the same key never refunds twice. Returns the created <c>refundId</c>.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentOrderService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentOrderService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentOrderService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        var refund = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, ct);

        var orders = await service.GetOrdersForBuyerAsync(request.BuyerId, ct);
        var order = orders.FirstOrDefault(o => o.Id == request.OrderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = new RefundDto
            {
                Id = refund.Id,
                Amount = refund.Amount,
                PayPalRefundId = refund.PayPalRefundId,
                Status = refund.Status,
                CreatedAt = refund.CreatedAt
            },
            Order = order?.ToDto()
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
