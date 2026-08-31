using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full or in part. Repeating the
/// request under the same idempotency key returns the original refund instead of refunding twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService,
                IReadRepository<Payment> paymentRepository, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService, paymentRepository, ct);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService,
        IReadRepository<Payment> paymentRepository, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }
        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.BadRequest(new { message = "amount must be positive when supplied." });
        }

        var refund = await paymentService.RefundAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer, ct);
        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId), ct);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            PaymentId = payment?.Id ?? 0,
            Status = refund.Status,
            Amount = refund.Amount,
            TotalRefunded = payment?.Refunds
                .Where(r => r.Status == PaymentRefundStatus.Completed || r.Status == PaymentRefundStatus.Pending)
                .Sum(r => r.Amount) ?? refund.Amount,
            Currency = payment?.Currency ?? string.Empty
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
