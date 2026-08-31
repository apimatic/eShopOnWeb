using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit to refund the full remaining captured amount.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating the request with the same key returns the original refund.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}

/// <summary>
/// Refunds a fulfilled (captured) order, in full or in part. Scoped to the caller's
/// own orders. Idempotent per idempotency key; distinct keys allow multiple partial
/// refunds, never exceeding the captured amount.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             RefundOrderRequest request,
             ClaimsPrincipal user,
             IRepository<Order> orderRepository,
             IRepository<Payment> paymentRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(orderId, request, user, orderRepository, paymentRepository, payPalClient);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IPayPalClient payPalClient)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound(new { message = $"Order {orderId} not found." });
        }

        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment == null)
        {
            return Results.NotFound(new { message = $"No payment exists for order {orderId}." });
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing != null)
        {
            return Results.Ok(Map(order, payment, existing.PayPalRefundId, existing.Status, existing.Amount));
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded) || payment.CaptureId == null)
        {
            return Results.Conflict(new { message = $"Order {orderId} is in state {order.Status} and cannot be refunded." });
        }

        var amount = request.Amount ?? payment.RefundableAmount;
        if (amount <= 0m || amount > payment.RefundableAmount)
        {
            return Results.UnprocessableEntity(new
            {
                message = $"Refund amount {amount} is invalid; the refundable amount for this order is {payment.RefundableAmount} {payment.Currency}."
            });
        }

        var refund = await payPalClient.RefundCaptureAsync(payment.CaptureId, amount, payment.Currency, request.IdempotencyKey);

        payment.AddRefund(request.IdempotencyKey, refund.RefundId, refund.Amount, refund.Status);
        order.MarkRefunded(payment.RefundableAmount <= 0m);

        await paymentRepository.UpdateAsync(payment);
        await orderRepository.UpdateAsync(order);

        return Results.Ok(Map(order, payment, refund.RefundId, refund.Status, refund.Amount));
    }

    private static RefundOrderResponse Map(Order order, Payment payment, string refundId, string status, decimal amount) => new RefundOrderResponse
    {
        RefundId = refundId,
        OrderId = order.Id,
        Status = status,
        Amount = amount,
        Currency = payment.Currency,
        TotalRefunded = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        OrderStatus = order.Status.ToString()
    };
}
