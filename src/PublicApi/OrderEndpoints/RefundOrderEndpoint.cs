using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Identifier of the refund that was created.</summary>
    public int RefundId { get; set; }

    public int OrderId { get; set; }
    public RefundDto Refund { get; set; } = new();
    public PaymentSummaryDto Payment { get; set; } = new();
}

/// <summary>
/// Returns a fulfilled order, in full or in part, by refunding the captured payment. Shopper-scoped
/// (acts only on the caller's own order) and idempotent on the caller's key. A partial refund can
/// never take the total refunded beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CallerIdentity.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required for a refund." });

        try
        {
            var refund = await service.RefundAsync(request.OrderId, request.BuyerId, request.IdempotencyKey, request.Amount);

            // Reload payment state so the response shows the roll-up (partially/fully refunded).
            var order = await service.GetOrderForBuyerAsync(request.OrderId, request.BuyerId);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                OrderId = request.OrderId,
                Refund = new RefundDto
                {
                    Id = refund.Id,
                    PayPalRefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status.ToString(),
                    CreatedAt = refund.CreatedAt
                },
                Payment = OrderMapping.ToPaymentSummary(order.Payment!)
            };
            return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
