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

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating under the same key must not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal RefundedTotal { get; set; }
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — return after fulfilment: refund the capture, full or partial.
/// The order owner or an operator may refund; a partial refund never exceeds what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RefundOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentConflictException("A refund requires a caller-supplied idempotencyKey.");
                }

                var buyerId = CallerIdentity.BuyerId(user);
                var isAdmin = CallerIdentity.IsAdministrator(user);

                var outcome = await paymentService.RefundAsync(
                    orderId, buyerId, isAdmin, request.Amount, request.IdempotencyKey, ct);

                var response = new RefundOrderResponse
                {
                    RefundId = outcome.Refund.PayPalRefundId,
                    OrderId = orderId,
                    Amount = outcome.Refund.Amount,
                    Currency = outcome.Refund.CurrencyCode,
                    Status = outcome.Refund.Status,
                    PaymentStatus = outcome.Payment.Status.ToString(),
                    RefundedTotal = outcome.Payment.RefundedAmount(),
                    RefundableRemaining = outcome.Payment.RefundableAmount()
                };
                return Results.Ok(response);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
