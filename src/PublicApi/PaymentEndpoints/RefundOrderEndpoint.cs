using System.Linq;
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

public class RefundOrderRequest
{
    /// <summary>The amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// A caller-supplied idempotency key. Repeating a request under the same key does not refund twice; two
    /// distinct partial refunds of the same capture use two different keys.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    /// <summary>The identifier of the created refund, returned as a top-level field.</summary>
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Refunds a captured payment (a return after fulfilment), in full or in part. A partly-refunded order never
/// becomes refundable beyond what was captured. Shopper-scoped to the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey.");
        }

        var refund = await _paymentService.RefundOrderAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);

        // Reload the payment so the response reflects the new refund totals.
        var orders = await _paymentService.GetMyOrdersAsync(request.BuyerId);
        var payment = orders.FirstOrDefault(o => o.Order.Id == request.OrderId)?.Payment;

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            OrderId = request.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            Payment = payment is null ? null : PaymentDto.From(payment)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.RefundId}", response);
    }
}
