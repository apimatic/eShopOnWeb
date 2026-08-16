using System.Security.Claims;
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
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Optional note shown to the payer.</summary>
    public string? Note { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refunds a fulfilled order, in full or in part.
/// Shopper-scoped (own order). Idempotent per idempotency key. Returns the refund id top-level.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.Request, IOrderPaymentService>
{
    public record Request(int OrderId, string BuyerId, RefundOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest body, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
                await HandleAsync(new Request(orderId, user.GetBuyerId(), body ?? new RefundOrderRequest()), paymentService))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            throw new PaymentException("An idempotency key is required for refunds.", PaymentErrorReason.Validation);
        }

        var (order, refund) = await paymentService.RefundAsync(
            request.BuyerId, request.OrderId, request.Body.Amount, request.Body.IdempotencyKey, request.Body.Note);

        var response = new RefundResponse
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            OrderId = order.Id,
            Payment = order.Payment is null ? null : PaymentDtoMapper.ToDto(order.Payment)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.RefundId}", response);
    }
}
