using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. Accepts either raw card
/// details or one of the caller's saved cards. Idempotent in effect: a re-post of an already
/// authorized (or paid) order returns the current state without holding funds again.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IRepository<SavedPaymentMethod> paymentMethodRepository,
                IPaymentProcessor processor,
                IOptions<PayPalSettings> settings,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
                if (order is null || order.BuyerId != buyerId)
                {
                    return Results.NotFound(new { message = $"Order {orderId} was not found." });
                }

                // Idempotent in effect — never authorize the shopper twice.
                if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Paid)
                {
                    return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
                }

                if (order.PaymentStatus != OrderPaymentStatus.PendingPayment)
                {
                    return Results.Conflict(new { message = $"Order {orderId} cannot be paid in its current state ({order.PaymentStatus})." });
                }

                CardDetails? card = null;
                string? vaultId = null;

                if (request.SavedPaymentMethodId.HasValue)
                {
                    var saved = await paymentMethodRepository.FirstOrDefaultAsync(
                        new SavedPaymentMethodByIdForBuyerSpecification(request.SavedPaymentMethodId.Value, buyerId), ct);
                    if (saved is null)
                    {
                        return Results.NotFound(new { message = "The specified saved card was not found." });
                    }
                    vaultId = saved.VaultToken;
                }
                else if (request.Card is not null)
                {
                    card = PaymentMapping.ToCardDetails(request.Card);
                }
                else
                {
                    return Results.BadRequest(new { message = "Provide either card details or a savedPaymentMethodId." });
                }

                var currency = settings.Value.Currency ?? "USD";

                // Globally-unique reference tied to this order, echoed to PayPal as custom_id for
                // reconciliation. Unique so it never collides with another transaction's reference.
                var reference = order.Payment?.CustomReference ?? $"ORD-{order.Id}-{Guid.NewGuid():N}";
                var authRequest = new PaymentAuthorizationRequest(reference, order.Total(), card, vaultId);

                var result = await processor.AuthorizeAsync(authRequest, $"auth-{order.Id}", ct);

                order.RecordAuthorization(currency, result.PayPalOrderId, result.AuthorizationId,
                    result.Status, DateTimeOffset.UtcNow, result.ExpiresAt, reference);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
