using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    /// <summary>Set from the route, not the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not refund
    /// twice; two distinct partial refunds use two distinct keys.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>The identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// Refunds a fulfilled order, in full or in part. Shopper-scoped. Never refunds beyond the captured
/// amount, and is idempotent under the caller-supplied key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);

        // A caller may repeat under the same key; if none is supplied we generate a stable one per
        // request so accidental double-clicks (which reuse the same HTTP body) still de-duplicate at
        // the API boundary is the caller's responsibility — so we require an explicit key.
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? throw new ArgumentException("An idempotencyKey is required for a refund.")
            : request.IdempotencyKey!;

        var (refund, payment) = await service.RefundAsync(buyerId, request.OrderId, request.Amount, idempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Payment = PaymentStateDto.From(payment)
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
