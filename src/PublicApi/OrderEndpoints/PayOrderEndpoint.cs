using System;
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
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo, IRepository<PaymentInfo> paymentRepo, IRepository<PaymentMethod> pmRepo, IPayPalService payPal, IOptions<PayPalSettings> settings, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
                if (order == null) return Results.NotFound();
                if (order.BuyerId != buyerId) return Results.Forbid();
                if (order.Status != OrderStatus.PendingPayment)
                    return Results.Conflict($"Order is already in state '{order.Status}'.");

                // Idempotency: return existing authorization if already authorized
                if (order.Payment != null && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
                {
                    return Results.Ok(new PayOrderResponse
                    {
                        PayPalOrderId = order.Payment.PayPalOrderId,
                        AuthorizationId = order.Payment.AuthorizationId,
                        ExpirationTime = order.Payment.AuthorizationId != null ? "see PayPal dashboard" : null
                    });
                }

                var currency = settings.Value.Currency;
                var amount = order.Total();
                var idempotencyKey = $"pay-{orderId}";

                PaymentAuthorizationResult authResult;
                if (request.PaymentMethodId.HasValue)
                {
                    // Saved card — look up the vault token from DB
                    var savedMethod = await pmRepo.FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpec(request.PaymentMethodId.Value, buyerId));
                    if (savedMethod == null)
                        return Results.NotFound($"Payment method {request.PaymentMethodId} not found.");
                    authResult = await payPal.AuthorizeWithVaultTokenAsync(amount, currency, idempotencyKey, savedMethod.PayPalTokenId);
                }
                else if (request.Card != null)
                {
                    var card = new CardPaymentDetails(
                        request.Card.Number,
                        request.Card.Expiry,
                        request.Card.SecurityCode,
                        request.Card.Name);
                    authResult = await payPal.AuthorizeWithCardAsync(amount, currency, idempotencyKey, card);
                }
                else
                {
                    return Results.BadRequest("Provide either card details or a paymentMethodId.");
                }

                var payment = new PaymentInfo(orderId, currency);
                payment.SetAuthorization(authResult.PayPalOrderId, authResult.AuthorizationId);
                await paymentRepo.AddAsync(payment);

                order.UpdateStatus(OrderStatus.Authorized);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new PayOrderResponse
                {
                    PayPalOrderId = authResult.PayPalOrderId,
                    AuthorizationId = authResult.AuthorizationId,
                    ExpirationTime = authResult.ExpirationTime
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
