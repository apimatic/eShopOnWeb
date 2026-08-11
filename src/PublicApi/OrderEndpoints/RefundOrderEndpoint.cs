using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never refunds twice;
    /// two distinct keys are two legitimate partial refunds. May also be supplied via the
    /// <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentState { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// Operator action: refunds a captured payment, in full or in part. A partly-refunded order can
/// never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Refunds a captured payment, full or partial (operator)", Tags = new[] { "OrderEndpoints" })]
            async (int orderId, RefundOrderRequest request, HttpRequest http, IPaymentService payments) =>
            {
                var key = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                    key = headerKey.ToString();
                if (string.IsNullOrWhiteSpace(key))
                    throw new PaymentValidationException(
                        "A refund requires an idempotency key (request body 'idempotencyKey' or 'Idempotency-Key' header).");

                var order = await payments.RefundAsync(orderId, request.Amount, key!);
                var payment = order.Payment!;
                var refund = payment.FindRefundByKey(key!)!;

                var response = new RefundOrderResponse
                {
                    RefundId = refund.PayPalRefundId,
                    OrderId = order.Id,
                    Amount = refund.Amount,
                    Currency = payment.Currency,
                    Status = refund.Status,
                    PaymentState = payment.State.ToString(),
                    TotalRefunded = payment.RefundedAmount,
                    RefundableRemaining = payment.RefundableAmount
                };
                return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
