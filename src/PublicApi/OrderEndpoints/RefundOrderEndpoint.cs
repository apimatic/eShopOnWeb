using System;
using System.Security.Claims;
using System.Linq;
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
/// Refund a captured payment in full or in part. The request carries a
/// caller-supplied idempotency key: repeating it under the same key returns the
/// original refund instead of moving money again, while distinct keys allow
/// several partial refunds of the same capture (never beyond what was captured).
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, string, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest body, HttpContext httpContext, ClaimsPrincipal principal, IPaymentProcessingService paymentProcessing) =>
            {
                var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
                return await HandleAsync(
                    new RefundOrderRequest(orderId, body, idempotencyKey),
                    principal.Identity?.Name ?? string.Empty,
                    paymentProcessing);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, string buyerId, IPaymentProcessingService paymentProcessing)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrEmpty(request.IdempotencyKey))
        {
            throw new ApplicationCore.Exceptions.DomainValidationException(
                "A caller-supplied idempotency key is required: send the Idempotency-Key header or an idempotencyKey body property.");
        }

        var refund = await paymentProcessing.RefundOrderAsync(
            buyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);

        response.RefundId = refund.RefundId;
        response.Status = refund.Status;
        response.Amount = refund.Amount;
        response.OrderId = request.OrderId;
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    /// <summary>Amount to refund; omitted or null = refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Caller-supplied idempotency key (body or Idempotency-Key header). Required.</summary>
    public string? IdempotencyKey { get; init; }

    public string? NoteToPayer { get; init; }

    public RefundOrderRequest() { }

    public RefundOrderRequest(int orderId, RefundOrderRequest source, string? headerIdempotencyKey)
    {
        OrderId = orderId;
        Amount = source.Amount;
        IdempotencyKey = headerIdempotencyKey ?? source.IdempotencyKey;
        NoteToPayer = source.NoteToPayer;
        _correlationId = source.CorrelationId();
    }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>PayPal's id for this refund.</summary>
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int OrderId { get; set; }
}
