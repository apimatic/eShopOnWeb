using System;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refund a fulfilled order, in full or in part. The caller-supplied idempotency key makes a repeat return the
/// same refund; two distinct keys are two distinct partial refunds. Refunds can never exceed the captured amount.
/// POST /api/orders/{orderId}/refunds
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentValidationException("An idempotencyKey is required for refunds.");

        var response = new RefundOrderResponse(request.CorrelationId());
        var refund = await service.RefundAsync(request.OrderId, request.BuyerId!, request.Amount, request.IdempotencyKey!);

        // Reload payment state for the response.
        response.OrderId = request.OrderId;
        response.RefundId = refund.Id;
        response.Refund = RefundDto.FromEntity(refund);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating under the same key returns the same refund.</summary>
    public string? IdempotencyKey { get; set; }

    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
