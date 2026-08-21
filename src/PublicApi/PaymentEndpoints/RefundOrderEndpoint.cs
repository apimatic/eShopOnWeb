using System.Security.Claims;
using System.Threading;
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
/// Returns money after fulfilment: refunds the captured payment, in full or in part. Carries a
/// caller-supplied idempotency key so a repeated request never refunds twice. Shopper-scoped.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequestDto request, IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    throw new PaymentStateException("An idempotency key is required for a refund.");

                var order = await paymentService.RefundOrderAsync(orderId, buyerId, request.Amount, request.IdempotencyKey, ct);
                var refund = order.Payment?.FindRefundByKey(request.IdempotencyKey);

                var response = new RefundOrderResponseDto
                {
                    RefundId = refund?.RefundId ?? string.Empty,
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Amount = refund?.Amount ?? 0m
                };
                return Results.Ok(response);
            })
            .Produces<RefundOrderResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
