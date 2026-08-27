using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full (omit amount) or in part.
/// The idempotency key is caller-supplied — repeating the request under the same key returns
/// the original refund; a distinct key performs a new partial refund. A partly-refunded order
/// can never be refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentConflictException("An idempotencyKey is required to issue a refund.");
        }
        if (request.Amount is <= 0m)
        {
            throw new PaymentConflictException("Refund amount must be positive.");
        }

        var refund = await paymentService.RefundPaymentAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            RefundId = refund.Id,
            Refund = RefundDto.FromRefund(refund)
        };
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Populated from the JWT; never trusted from the request body.</summary>
    public string? BuyerId { get; set; }

    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key for this refund.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
