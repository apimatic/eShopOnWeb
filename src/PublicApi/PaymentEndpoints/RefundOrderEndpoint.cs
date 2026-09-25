using System.Security.Claims;
using System.Text.Json.Serialization;
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
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. A repeat under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken CancellationToken { get; set; }
}

public record RefundResponse(string RefundId, int OrderId, decimal Amount, string Status);

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds the shopper's own fulfilled order, in full or in
/// part, idempotently. Returns <c>refundId</c> as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext ctx) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CallerIdentity.BuyerId(user);
                request.CancellationToken = ctx.RequestAborted;
                return await HandleAsync(request, service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new InvalidOrderPaymentStateException("A refund requires a caller-supplied idempotencyKey.");

        var refund = await service.RefundAsync(request.OrderId, request.BuyerId, request.Amount,
            request.IdempotencyKey, request.CancellationToken);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.PayPalRefundId}",
            new RefundResponse(refund.PayPalRefundId, request.OrderId, refund.Amount, refund.Status));
    }
}
