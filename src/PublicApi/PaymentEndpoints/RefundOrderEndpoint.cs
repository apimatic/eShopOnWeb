using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not refund
    /// twice; two distinct keys yield two distinct (partial) refunds.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public OrderView Order { get; set; } = default!;
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a fulfilled order, fully or partially, under a
/// caller-supplied idempotency key. Never refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, PayPalConfiguration>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user,
             IOrderPaymentService service, PayPalConfiguration config) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, config);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, PayPalConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException("A refund requires a caller-supplied idempotencyKey.");
        }
        if (request.Amount is <= 0m)
        {
            throw new PaymentOperationException("Refund amount, when supplied, must be positive.");
        }

        var (order, refund) = await service.RefundOrderAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey!);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            OrderId = order.Id,
            Order = OrderView.From(order, config.Currency)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}
