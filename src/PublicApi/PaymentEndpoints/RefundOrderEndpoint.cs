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
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the refund, so the flow can be driven end to end.</summary>
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// Refunds a captured order — fully or partially — under a caller-supplied idempotency key.
/// A partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException(PaymentErrorReason.Validation, "A refund requires a caller-supplied idempotencyKey.");
        }

        var refund = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey!);
        var order = await service.GetOrderForBuyerAsync(request.BuyerId, request.OrderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderId = request.OrderId,
            OrderStatus = order?.Status.ToString() ?? string.Empty,
            RefundedAmount = order?.Payment?.RefundedAmount ?? refund.Amount,
            RefundableRemaining = order?.Payment?.RefundableRemaining ?? 0m
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
