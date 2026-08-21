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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — vault a card for the signed-in shopper and return a safe descriptor
/// (brand / last four / expiry) plus the saved-card id. Full card details are never stored. Shopper-scoped.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                IRepository<SavedPaymentMethod> paymentMethodRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    return Results.BadRequest(new { message = "Card details are required to save a payment method." });
                }

                var card = PaymentMapping.ToCardDetails(request.Card);
                var vaulted = await processor.VaultCardAsync(card, buyerId, Guid.NewGuid().ToString("N"), ct);

                var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4,
                    vaulted.Expiry, request.Alias, DateTimeOffset.UtcNow);
                saved = await paymentMethodRepository.AddAsync(saved, ct);

                var response = new SavePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    Expiry = saved.Expiry,
                    Alias = saved.Alias
                };

                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
