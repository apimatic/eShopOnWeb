using System;
using System.Security.Claims;
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
/// Operator: refunds a fulfilled order's captured payment, in full (amount omitted) or in part.
/// The idempotency key is caller-supplied: repeating under the same key returns the original
/// refund; distinct keys remain legitimate separate partial refunds, never exceeding the capture.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }
        if (request.Amount is <= 0m)
        {
            return Results.BadRequest(new { message = "amount must be positive when provided." });
        }

        var refund = await _paymentService.RefundOrderAsync(
            request.OrderId, request.Amount, request.IdempotencyKey, request.Note, CancellationToken.None);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Null = refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; a repeat under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }
}
