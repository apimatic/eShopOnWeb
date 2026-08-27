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

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    /// <summary>Partial amount; omit to refund the remaining refundable balance in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns the
    /// original refund; a distinct key is a distinct (partial) refund.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public RefundDto? Refund { get; set; }
}

/// <summary>
/// Refunds a captured payment, in full or in part, after fulfilment. A partly-refunded order
/// can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        if (request.BuyerId == null)
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }
        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.BadRequest("A refund amount must be positive.");
        }

        var refund = await paymentService.RefundAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, default);

        if (refund == null)
        {
            return Results.NotFound();
        }

        response.RefundId = refund.RefundId;
        response.OrderId = request.OrderId;
        response.Refund = new RefundDto
        {
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            CreatedAt = refund.CreatedAt
        };
        return Results.Ok(response);
    }
}
