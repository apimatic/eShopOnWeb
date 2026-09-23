using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/payment-methods — save a card for the signed-in shopper. Returns a safe descriptor + id.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService cards, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var saved = await cards.SaveAsync(buyerId, request.ToCardDetails(), ct);
                return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.PaymentMethodId,
                    Brand = saved.Brand,
                    LastDigits = saved.LastDigits,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName,
                });
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards (shopper).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISavedCardService cards, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var list = await cards.ListAsync(buyerId, ct);
                return Results.Ok(list);
            })
            .Produces<IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card (shopper).</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, ISavedCardService cards, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                await cards.DeleteAsync(buyerId, paymentMethodId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");
    }
}
