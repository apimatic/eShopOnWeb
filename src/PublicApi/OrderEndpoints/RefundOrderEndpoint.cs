using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part, for the shopper's own order. Idempotent by
/// caller-supplied key: repeating a request under the same key returns the original refund
/// instead of refunding twice. The running total of refunds can never exceed what was captured.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal, IRepository<Order>>
{
    private readonly IPaymentProvider _paymentProvider;

    public RefundOrderEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequestBody body, ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new RefundOrderRequest(orderId, body), user, orderRepository);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "IdempotencyKey is required." });
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdForBuyerSpec(request.OrderId, buyerId));
        if (order is null)
        {
            return Results.NotFound();
        }

        var existingRefund = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existingRefund is not null)
        {
            return Results.Ok(new RefundOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                RefundId = existingRefund.Id,
                PayPalRefundId = existingRefund.RefundId,
                Amount = existingRefund.Amount,
                Status = existingRefund.Status,
                Order = OrderDto.FromOrder(order)
            });
        }

        if (order.Payment?.CapturedAmount is null || order.Payment.CaptureId is null)
        {
            return Results.Conflict(new { message = $"Order {order.Id} has no captured payment to refund (status {order.Status})." });
        }

        var remaining = order.Payment.CapturedAmount.Value - order.Payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining)
        {
            return Results.BadRequest(new { message = $"Amount must be greater than 0 and at most {remaining:0.00} {order.Currency} (the remaining refundable balance)." });
        }

        var result = await _paymentProvider.RefundAsync(
            order.Payment.CaptureId,
            amount,
            order.Currency,
            $"refund-{order.Id}-{order.IdempotencySalt:N}-{request.IdempotencyKey}",
            CancellationToken.None);

        var refund = order.RecordRefund(result.RefundId, request.IdempotencyKey, amount, result.Status, System.DateTimeOffset.UtcNow);
        await orderRepository.UpdateAsync(order);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            RefundId = refund.Id,
            PayPalRefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Order = OrderDto.FromOrder(order)
        });
    }
}
