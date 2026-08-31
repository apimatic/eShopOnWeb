using System;
using System.Security.Claims;
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
/// Refunds a fulfilled order, in full or in part. The caller-supplied idempotency key
/// guarantees a repeated request never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var response = new RefundOrderResponse(request.CorrelationId());

        var refund = await orderPaymentService.RefundOrderAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.NoteToPayer);

        response.RefundId = refund.Id;
        response.OrderId = request.OrderId;
        response.Refund = RefundDto.FromRefund(refund);
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>
    /// Partial amount to refund; omit to refund the remaining refundable amount.
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key never
    /// refunds twice; distinct keys allow legitimate separate partial refunds.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public RefundDto? Refund { get; set; }
}
