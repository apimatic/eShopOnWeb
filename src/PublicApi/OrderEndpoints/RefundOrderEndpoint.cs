using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order, in full or in part. The caller-supplied idempotency key
/// guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromRoute] int orderId, [FromBody] RefundOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("'idempotencyKey' is required.");
        }
        if (request.Amount is <= 0m)
        {
            return Results.BadRequest("'amount' must be positive when provided.");
        }

        var refund = await paymentService.RefundOrderAsync(
            buyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);
        var payment = await paymentService.GetPaymentForOrderAsync(request.OrderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.PayPalRefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            TotalRefunded = payment?.TotalRefunded ?? refund.Amount,
            RemainingRefundable = payment?.RefundableAmount ?? 0m
        };
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}
