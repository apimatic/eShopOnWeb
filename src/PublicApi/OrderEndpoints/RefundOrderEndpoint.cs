using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Body of POST /api/orders/{orderId}/refunds. The idempotency key is caller-supplied: repeating a
/// request under the same key must not refund twice, while two distinct keys are two legitimate refunds.
/// </summary>
public record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public record RefundOrderCommand(int OrderId, RefundOrderRequest? Body);

public record RefundOrderResponse(
    int RefundId,
    string PayPalRefundId,
    int OrderId,
    decimal Amount,
    string Currency,
    string Status,
    decimal RefundableRemaining);

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a fulfilled order's capture, in full or in part.
/// Shopper-scoped (the caller's own order). A partly-refunded order never becomes refundable beyond
/// what was captured. Returns the refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? body, IPaymentService paymentService) =>
                await HandleAsync(new RefundOrderCommand(orderId, body), paymentService))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        var body = command.Body;
        if (body is null || string.IsNullOrWhiteSpace(body.IdempotencyKey))
            throw new PaymentException("A refund requires an idempotencyKey.");

        var (refund, order) = await paymentService.RefundAsync(buyerId, command.OrderId, body.Amount, body.IdempotencyKey);

        var response = new RefundOrderResponse(
            RefundId: refund.Id,
            PayPalRefundId: refund.PayPalRefundId,
            OrderId: command.OrderId,
            Amount: refund.Amount,
            Currency: refund.Currency,
            Status: refund.Status,
            RefundableRemaining: order.Payment?.RefundableRemaining ?? 0m);

        return Results.Created($"api/orders/{command.OrderId}/refunds/{refund.Id}", response);
    }
}
