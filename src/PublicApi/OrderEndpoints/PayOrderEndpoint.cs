using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardInputModel? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Pays for an order with PayPal, using either supplied card details or one of the shopper's saved
/// cards. Idempotent in effect: an already-paid order is returned as-is (no second charge), and the
/// PayPal request carries a stable idempotency key so a concurrent double-click cannot charge twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user,
                   IRepository<Order> orderRepository, IRepository<SavedPaymentMethod> paymentMethodRepository,
                   IPaymentService paymentService, CancellationToken ct) =>
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

                // Idempotent outcomes for a repeated request.
                if (order.PaymentStatus == OrderPaymentStatus.Paid)
                {
                    return Results.Ok(Describe(order, "Order is already paid."));
                }
                if (order.PaymentStatus == OrderPaymentStatus.Refunded)
                {
                    return Results.Conflict(Describe(order, "Order has been refunded and cannot be paid."));
                }

                var amount = new PaymentAmount(order.Total());

                CardPaymentResult result;
                if (request.PaymentMethodId.HasValue)
                {
                    var savedCard = await paymentMethodRepository.FirstOrDefaultAsync(
                        new SavedPaymentMethodByIdSpecification(request.PaymentMethodId.Value, buyerId), ct);
                    if (savedCard is null)
                    {
                        return Results.NotFound($"Saved card {request.PaymentMethodId.Value} was not found.");
                    }

                    var key = $"pay-{order.PaymentReference}-pm{savedCard.Id}";
                    result = await paymentService.ChargeOrderWithVaultedCardAsync(amount, savedCard.VaultId, key, ct);
                }
                else if (request.Card is { } card && card.HasCardNumber)
                {
                    var key = $"pay-{order.PaymentReference}-{card.Fingerprint()}";
                    result = await paymentService.ChargeOrderWithCardAsync(amount, card.ToCardDetails(), key, ct);
                }
                else
                {
                    return Results.BadRequest("Provide either card details or a saved paymentMethodId.");
                }

                order.MarkPaid(result.PayPalOrderId, result.CaptureId);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(Describe(order, "Payment captured."));
            })
            .Produces<PayOrderResponse>(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Pays for an order with PayPal", "Pays with supplied card details or a saved card."));
    }

    private static PayOrderResponse Describe(Order order, string message) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        Message = message
    };
}
