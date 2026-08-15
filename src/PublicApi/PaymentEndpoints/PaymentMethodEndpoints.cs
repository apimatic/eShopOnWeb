using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentEndpointHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/payment-methods — save (vault) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                var saved = await service.SaveCardAsync(buyerId, request.Card.ToDomain(), request.Label, ct);

                return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.CardBrand,
                    LastFourDigits = saved.LastFourDigits,
                    ExpiryMonth = saved.ExpiryMonth,
                    ExpiryYear = saved.ExpiryYear,
                    Label = saved.Label
                });
            }))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                var cards = await service.GetForBuyerAsync(buyerId, ct);
                return Results.Ok(cards.Select(SavedCardDto.From).ToList());
            }))
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethods");
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                int paymentMethodId,
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                await service.RemoveAsync(buyerId, paymentMethodId, ct);
                return Results.NoContent();
            }))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");
    }
}
