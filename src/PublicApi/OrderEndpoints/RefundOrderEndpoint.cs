using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Partial amount to refund; omit to refund the remaining captured amount in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns the
    /// original refund instead of refunding again.
    /// </summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Populated from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) {}
    public RefundOrderResponse() {}

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public decimal RefundedTotal { get; set; }
    public decimal RemainingRefundable { get; set; }
    public bool AlreadyExisted { get; set; }
}

/// <summary>
/// Refunds a fulfilled order's captured payment, in full or in part, for the order's owner.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "IdempotencyKey is required." });
        }

        var (order, refund, alreadyExisted) = await orderPaymentService.RefundAsync(
            request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey, request.Note);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Amount = refund.Amount,
            Currency = refund.Currency,
            RefundedTotal = order.RefundedAmount,
            RemainingRefundable = order.RefundableAmount(),
            AlreadyExisted = alreadyExisted
        };
        return Results.Ok(response);
    }
}
