using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// HTTP surface for the saved-cards flow. All actions are shopper-scoped: a card belongs to the shopper
/// who saved it, and one shopper can never see, use or delete another's.
/// </summary>
public static class SavedCardEndpoints
{
    // JWT scheme named explicitly — the host's default challenge scheme is the Identity cookie.
    private static AuthorizeAttribute ShopperAuth() =>
        new() { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

    public static IEndpointRouteBuilder MapSavedCardEndpoints(this IEndpointRouteBuilder app)
    {
        // Save a card for the signed-in shopper.
        app.MapPost("api/payment-methods", async (
                SavePaymentMethodRequest request, ClaimsPrincipal user,
                IPaymentMethodService service, CancellationToken ct) =>
            {
                if (request.Card is null)
                    return Results.BadRequest(new { message = "Card details are required." });

                var method = await service.SaveAsync(user.GetBuyerId(), request.Card.ToCardDetails(), ct);
                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = method.Id,
                    Brand = method.Brand,
                    LastDigits = method.LastDigits,
                    Expiry = method.Expiry,
                    CardholderName = method.CardholderName
                };
                return Results.Created($"api/payment-methods/{method.Id}", response);
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");

        // The caller's saved cards.
        app.MapGet("api/payment-methods", async (
                ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var methods = await service.ListAsync(user.GetBuyerId(), ct);
                var response = new PaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(m => m.ToDto()).ToList()
                };
                return Results.Ok(response);
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentMethods");

        // Remove a saved card.
        app.MapDelete("api/payment-methods/{paymentMethodId:int}", async (
                int paymentMethodId, ClaimsPrincipal user,
                IPaymentMethodService service, CancellationToken ct) =>
            {
                var removed = await service.DeleteAsync(user.GetBuyerId(), paymentMethodId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .RequireAuthorization(ShopperAuth())
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethods");

        return app;
    }
}
