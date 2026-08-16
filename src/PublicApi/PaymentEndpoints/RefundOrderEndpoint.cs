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
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never refunds twice;
    /// two distinct partial refunds use two distinct keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public PaymentView Payment { get; set; } = default!;
}

/// <summary>
/// Refunds a captured payment for the shopper's own order, in full or in part. A partly-refunded order
/// never becomes refundable beyond what was captured. Returns the refundId.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.BuyerId();

                // Allow the idempotency key to arrive via header as well as body.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, service, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentFlowException("A refund requires a caller-supplied idempotency key (body 'idempotencyKey' or 'Idempotency-Key' header).", 400);
        }

        var (refundId, payment) = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, ct);
        return Results.Ok(new RefundOrderResponse { RefundId = refundId, Payment = payment });
    }
}
