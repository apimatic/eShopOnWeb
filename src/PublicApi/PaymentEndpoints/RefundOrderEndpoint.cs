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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    /// <summary>Amount to refund. Omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not refund
    /// twice; two distinct partial refunds must use two distinct keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = default!;

    [JsonIgnore]
    public string BuyerId { get; set; } = default!;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
}

/// <summary>Refunds a captured payment, full or partial. Shopper-scoped to the caller's own order.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.BuyerId = ApiCaller.BuyerId(user);
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        var refund = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount
        });
    }
}
