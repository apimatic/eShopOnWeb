using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key won't refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the created refund.</summary>
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — shopper-scoped. Refund the captured payment, full or partial.
/// A partly-refunded order never becomes refundable beyond what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    private readonly IOrderPaymentService _orders;
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly PayPalSettings _settings;

    public RefundOrderEndpoint(IOrderPaymentService orders, IReadRepository<Order> orderReadRepository, PayPalSettings settings)
    {
        _orders = orders;
        _orderReadRepository = orderReadRepository;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RefundOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext http)
    {
        var ct = http.RequestAborted;
        var buyerId = http.BuyerId();
        var orderId = http.RouteInt("orderId");

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }

        var refund = await _orders.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);

        // Reload to report the remaining refundable amount after this refund.
        var order = await _orderReadRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        var remaining = order?.Payment?.RefundableRemaining() ?? 0m;

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Currency = _settings.Currency,
            RefundableRemaining = remaining
        };
        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}
