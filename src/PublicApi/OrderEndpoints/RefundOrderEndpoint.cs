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
/// Refunds a fulfilled order, in full or in part. The idempotency key guarantees a repeated
/// request under the same key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;

        var refund = await _orderPaymentService.RefundOrderAsync(
            request.OrderId, buyerId, request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status
        };
        return Results.Ok(response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating the request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) {}
    public RefundOrderResponse() {}

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}
