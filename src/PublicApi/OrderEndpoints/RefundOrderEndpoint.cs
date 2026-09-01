using System;
using System.Threading;
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
/// Operator: refund a fulfilled (captured) order, in full or in part.
/// The idempotencyKey guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var outcome = await paymentService.RefundOrderAsync(orderId, request.Amount,
                    request.IdempotencyKey ?? string.Empty, request.NoteToPayer, cancellationToken);

                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = outcome.Refund.Id,
                    OrderId = orderId,
                    PayPalRefundId = outcome.Refund.PayPalRefundId,
                    Status = outcome.Refund.Status,
                    Amount = outcome.Refund.Amount,
                    Currency = outcome.Payment.Currency,
                    TotalRefunded = outcome.Payment.TotalRefunded,
                    RemainingRefundable = outcome.Payment.RefundableAmount,
                    PaymentStatus = outcome.Payment.Status.ToString(),
                    AlreadyExisted = outcome.AlreadyExisted
                };
                return Results.Ok(response);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public bool AlreadyExisted { get; set; }
}
