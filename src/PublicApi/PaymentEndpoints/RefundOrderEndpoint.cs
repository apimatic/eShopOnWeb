using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: a repeat under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured order in full or in part. Shopper-scoped.
/// The new refund's identifier is returned as a top-level <c>refundId</c>.
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
                IOrderPaymentService service,
                IRepository<Payment> paymentRepository,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new OrderValidationException("A refund requires a non-empty 'idempotencyKey'.");
                }

                var refund = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

                var payment = await paymentRepository.FirstOrDefaultAsync(
                    new ApplicationCore.Specifications.PaymentByOrderIdSpecification(orderId), ct);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = refund.Id,
                    Refund = RefundDto.From(refund),
                    Payment = payment is null ? new PaymentStateDto() : PaymentStateDto.From(payment)
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
