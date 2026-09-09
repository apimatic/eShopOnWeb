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

/// <summary>Returns (refunds) a captured payment, in full or in part, under a caller idempotency key.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, RefundRequest request, ClaimsPrincipal user,
                IPaymentService paymentService, CancellationToken ct) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException("An idempotencyKey is required for a refund.");
                }

                var buyerId = user.GetBuyerId();
                var (order, refund) = await paymentService.RefundAsync(orderId, buyerId, request.Amount,
                    request.IdempotencyKey, ct);

                return Results.Ok(new
                {
                    refundId = refund.PayPalRefundId,
                    orderId = order.Id,
                    amount = refund.Amount,
                    currency = refund.Currency,
                    status = refund.Status,
                    orderStatus = order.Status.ToString(),
                    totalRefunded = order.Payment!.TotalRefunded(),
                    refundableRemaining = order.Payment!.RefundableRemaining()
                });
            })
            .WithTags("OrderEndpoints");
    }
}
