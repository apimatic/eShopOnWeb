using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key
    /// never refunds twice; two distinct keys are two legitimate partial refunds.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Refunds a fulfilled order in full or in part. Shopper-scoped: acts only on the caller's
/// own order. Idempotent per caller-supplied key; never refundable beyond what was captured.
/// Returns the refund's id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.CallerId = PaymentEndpointHelpers.GetCallerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for refunds.");
        }

        var (order, refund) = await service.RefundAsync(request.OrderId, request.CallerId, request.IdempotencyKey, request.Amount);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status,
                CreatedAt = refund.CreatedAt
            },
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}
