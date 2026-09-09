using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/payment-methods — save a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user) =>
            {
                var identity = CallerIdentity.Require(user);
                if (request?.Card is null)
                {
                    return Results.BadRequest(new { message = "A 'card' is required." });
                }

                var method = await service.SaveCardAsync(identity, request.Card.ToCardDetails(), request.Alias);

                // paymentMethodId returned as a top-level field; the card is described safely, never in full.
                return Results.Created($"api/payment-methods/{method.Id}", new
                {
                    paymentMethodId = method.Id,
                    brand = method.Brand,
                    last4 = method.Last4,
                    expiry = method.Expiry,
                    alias = method.Alias
                });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>GET /api/payment-methods — the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedCardService service, ClaimsPrincipal user) =>
            {
                var identity = CallerIdentity.Require(user);
                var cards = await service.ListCardsAsync(identity);
                return Results.Ok(cards.Select(c => c.ToDto()).ToList());
            })
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer
/// appears among the caller's cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedCardService service, ClaimsPrincipal user) =>
            {
                var identity = CallerIdentity.Require(user);
                await service.DeleteCardAsync(identity, paymentMethodId);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
