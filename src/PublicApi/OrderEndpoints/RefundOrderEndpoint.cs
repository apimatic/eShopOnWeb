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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Fully refunds an order's captured payment. Idempotent in effect: an already-refunded order is
/// returned as-is, and the PayPal request carries a stable idempotency key so a double-click cannot
/// refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ClaimsPrincipal user,
                   IRepository<Order> orderRepository, IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderRepository.FirstOrDefaultAsync(new CustomerOrderByIdSpecification(orderId, buyerId), ct);
                if (order is null)
                {
                    return Results.NotFound($"Order {orderId} was not found.");
                }

                if (order.PaymentStatus == OrderPaymentStatus.Refunded)
                {
                    return Results.Ok(Describe(order, "Order is already refunded."));
                }
                if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
                {
                    return Results.Conflict(Describe(order, "Order has no captured payment to refund."));
                }

                var key = $"refund-{order.PaymentReference}";
                var result = await paymentService.RefundAsync(order.PayPalCaptureId, key, ct);

                order.MarkRefunded(result.RefundId);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(Describe(order, "Payment refunded."));
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Refunds an order's payment in full", "Issues a full PayPal refund of the captured payment."));
    }

    private static RefundOrderResponse Describe(Order order, string message) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus.ToString(),
        RefundId = order.PayPalRefundId,
        Message = message
    };
}
