using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key reports the
    /// original refund instead of refunding again; distinct keys allow distinct partial refunds.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string RefundStatus { get; set; } = string.Empty;
    public bool Replayed { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

/// <summary>
/// Operator action: refunds the captured payment, in full or in part, after fulfilment.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public RefundOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request)
    {
        var result = await _orderPaymentService.RefundAsync(request.OrderId, request.Amount, request.IdempotencyKey, request.Note);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.Refund.Id,
            PayPalRefundId = result.Refund.PayPalRefundId,
            OrderId = result.Order.Id,
            OrderStatus = result.Order.Status.ToString(),
            Amount = result.Refund.Amount,
            Currency = result.Refund.Currency,
            RefundStatus = result.Refund.Status,
            Replayed = result.Replayed,
            TotalRefunded = result.Payment.TotalRefunded,
            RefundableRemaining = result.Payment.RefundableAmount
        };
        return Results.Ok(response);
    }
}
