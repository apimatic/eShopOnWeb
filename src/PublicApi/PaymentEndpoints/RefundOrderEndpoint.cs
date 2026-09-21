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

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for the full remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — repeating it must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public record RefundOrderResponse(
    string RefundId,
    string Status,
    decimal Amount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    string OrderStatus);

/// <summary>POST /api/orders/{orderId}/refunds — refunds a captured payment, in full or in part.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    throw new PaymentValidationException("An idempotencyKey is required for refunds.");

                var outcome = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, request.Note, ct);
                var response = new RefundOrderResponse(
                    RefundId: outcome.Refund.PayPalRefundId,
                    Status: outcome.Refund.Status,
                    Amount: outcome.Refund.Amount,
                    TotalRefunded: outcome.Payment.TotalRefunded(),
                    RefundableRemaining: outcome.Payment.RefundableRemaining(),
                    OrderStatus: outcome.Payment.Status.ToString());
                return Results.Ok(response);
            }))
            .Produces<RefundOrderResponse>()
            .WithTags("Payments");
    }
}
