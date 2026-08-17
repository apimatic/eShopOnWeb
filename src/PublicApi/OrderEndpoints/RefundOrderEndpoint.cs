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

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key returns
    /// the same refund rather than issuing a second one.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the refund.</summary>
    public string RefundId { get; set; } = string.Empty;
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a captured order in full or in part. Allowed for
/// the owning shopper or an administrator. A partly-refunded order never becomes refundable
/// beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(orderId, request, user, service))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.Json(new { statusCode = 400, message = "An idempotencyKey is required for refunds." }, statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var (order, refundId) = await service.RefundAsync(
                orderId, buyerId, user.IsAdministrator(), request.Amount, request.IdempotencyKey);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refundId,
                Order = OrderDto.From(order)
            };
            return Results.Created($"api/orders/{orderId}/refunds/{refundId}", response);
        }
        catch (Exception ex) when (PaymentErrorMapper.TryMap(ex, out var result))
        {
            return result;
        }
    }
}
