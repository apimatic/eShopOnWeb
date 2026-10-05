using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund; omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-chosen key; repeating a request with the same key never refunds twice. May also be sent as the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PayPalRefundId { get; set; }
    public string? PayPalStatus { get; set; }
    /// <summary>True when this key had already been used and the existing refund is returned.</summary>
    public bool Replayed { get; set; }
    public RefundDto? Refund { get; set; }
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Returns money after fulfilment: refunds the captured payment in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
             ClaimsPrincipal user, PaymentService service) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = user.BuyerId();
                request.IdempotencyKey ??= idempotencyKeyHeader;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentService service)
    {
        if (request.BuyerId is null)
            return Results.Unauthorized();

        var result = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey?.Trim(), CancellationToken.None);
        var refund = OrderDtoMapper.ToDto(result.Refund);
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.Refund.Id,
            OrderId = result.Order.Id,
            Amount = result.Refund.Amount,
            Currency = result.Payment.Currency,
            Status = refund.Status,
            PayPalRefundId = refund.PayPalRefundId,
            PayPalStatus = refund.PayPalStatus,
            Replayed = result.Replayed,
            Refund = refund,
            Payment = OrderDtoMapper.ToDto(result.Payment),
        };
        return result.Replayed
            ? Results.Ok(response)
            : Results.Created($"api/my-orders", response);
    }
}
