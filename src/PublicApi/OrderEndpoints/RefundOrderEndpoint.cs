using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order: refunds the captured payment, in full or in part.
/// Shopper-scoped — only the order's owner can refund it.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;

    public RefundOrderEndpoint(
        IRepository<Order> orderRepository,
        IPaymentGateway paymentGateway,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, request, user, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
        HandleAsync(orderId, request, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        // Idempotent replay under a known key: return the original refund, never refund twice.
        var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing is not null)
        {
            return Results.Ok(ToResponse(request, order, existing));
        }

        if (order.Status != OrderStatus.Fulfilled || order.CaptureId is null)
        {
            throw new PaymentStateException($"Order {order.Id} is {order.Status}; only fulfilled orders can be refunded.");
        }

        var currency = order.Currency ?? _payPalSettings.Currency;
        var amount = request.Amount ?? order.RefundableRemaining();
        if (amount <= 0)
        {
            throw new PaymentStateException($"Order {order.Id} has nothing left to refund.");
        }

        // Enforce the captured-amount ceiling locally before any money moves at PayPal.
        if (amount > order.RefundableRemaining())
        {
            throw new PaymentStateException(
                $"Refund of {amount} {currency} exceeds the refundable remainder " +
                $"({order.RefundableRemaining()} {currency} of {order.CapturedAmount} {currency} captured).");
        }

        // Always send an explicit amount: an empty refund body means "full capture" at PayPal,
        // which would overshoot when earlier partial refunds exist.
        var refund = await _paymentGateway.RefundCaptureAsync(
            order.CaptureId, amount, currency, PaymentKeys.RefundKey(request.IdempotencyKey), ct);

        // AddRefund enforces the captured-amount ceiling (throws PaymentStateException).
        var recorded = order.AddRefund(refund.RefundId, amount, currency, refund.Status, request.IdempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);

        return Results.Ok(ToResponse(request, order, recorded));
    }

    private static RefundOrderResponse ToResponse(RefundOrderRequest request, Order order, OrderRefund refund) =>
        new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            TotalRefunded = order.TotalRefunded(),
            RefundableRemaining = order.RefundableRemaining()
        };
}
