using System;
using System.ComponentModel.DataAnnotations;
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

/// <summary>
/// Refunds a fulfilled order's captured payment, in full (no amount) or in part.
/// Repeating a request under the same idempotency key never refunds twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var refund = await paymentService.RefundOrderAsync(
            CreateOrderEndpoint.GetBuyerId(user),
            isAdmin,
            request.OrderId,
            request.Amount,
            request.IdempotencyKey);

        response.RefundId = refund.RefundId;
        response.PayPalRefundId = refund.PayPalRefundId;
        response.PaymentId = refund.PaymentId;
        response.OrderId = refund.OrderId;
        response.Amount = refund.Amount;
        response.Currency = refund.Currency;
        response.Status = refund.Status;
        response.TotalRefunded = refund.TotalRefunded;
        response.RemainingRefundable = refund.RemainingRefundable;

        return Results.Created($"api/orders/{refund.OrderId}/refunds/{refund.RefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit for a full refund of the remaining captured amount.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; a repeat under the same key returns the original refund.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
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
    public string PayPalRefundId { get; set; } = string.Empty;
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
}
