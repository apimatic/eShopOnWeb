using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   PayOrderRequest req,
                   IRepository<Order> orderRepo,
                   IReadRepository<SavedCard> cardRepo,
                   IPayPalService payPal,
                   IOptions<PayPalSettings> settings,
                   ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId));
                if (order == null || order.BuyerId != buyerId)
                    return Results.NotFound();

                if (order.Status == OrderStatus.PaymentAuthorized)
                    return Results.Ok(new { message = "Already authorized.", authorizationId = order.AuthorizationId });

                if (order.Status != OrderStatus.PendingPayment)
                    return Results.BadRequest(new { error = $"Order is in status {order.Status} and cannot be paid." });

                var currency = settings.Value.Currency;
                var amount = order.Total();
                var orderRef = order.Id.ToString();

                PayPal.AuthorizeResult authResult;
                try
                {
                    if (req.PaymentMethodId.HasValue)
                    {
                        var card = await cardRepo.FirstOrDefaultAsync(
                            new SavedCardByIdAndBuyerSpec(req.PaymentMethodId.Value, buyerId));
                        if (card == null)
                            return Results.BadRequest(new { error = "Saved card not found or does not belong to you." });

                        authResult = await payPal.AuthorizeWithVaultTokenAsync(amount, currency, card.VaultToken, orderRef);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(req.CardNumber) || string.IsNullOrEmpty(req.CardExpiry) ||
                            string.IsNullOrEmpty(req.CardCvv) || string.IsNullOrEmpty(req.BillingCountry))
                            return Results.BadRequest(new { error = "Card details or paymentMethodId are required." });

                        var cardDetails = new CardDetails(
                            Number: req.CardNumber,
                            Expiry: req.CardExpiry,
                            Cvv: req.CardCvv,
                            CardholderName: req.CardholderName ?? "Cardholder",
                            BillingCountry: req.BillingCountry,
                            BillingStreet: req.BillingStreet,
                            BillingCity: req.BillingCity,
                            BillingState: req.BillingState,
                            BillingZip: req.BillingZip);

                        authResult = await payPal.AuthorizeWithCardAsync(amount, currency, cardDetails, orderRef);
                    }
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                order.SetPaymentAuthorized(authResult.PayPalOrderId, authResult.AuthorizationId);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new
                {
                    orderId = order.Id,
                    status = order.Status.ToString(),
                    payPalOrderId = order.PayPalOrderId,
                    authorizationId = order.AuthorizationId
                });
            })
            .WithTags("OrderEndpoints");
    }
}

public record PayOrderRequest(
    int? PaymentMethodId,
    string? CardNumber,
    string? CardExpiry,
    string? CardCvv,
    string? CardholderName,
    string? BillingCountry,
    string? BillingStreet,
    string? BillingCity,
    string? BillingState,
    string? BillingZip);
