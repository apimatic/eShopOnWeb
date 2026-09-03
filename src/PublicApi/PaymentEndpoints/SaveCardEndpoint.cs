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

/// <summary>Saves a card for the signed-in shopper. Returns a safe description, never full card details.</summary>
public class SaveCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ClaimsPrincipal user, IPaymentMethodService svc, CancellationToken ct) =>
                await PaymentEndpointHelpers.Guarded(user, async buyerId =>
                {
                    var c = request.Card;
                    var card = new CardDetails(c.CardholderName, c.Number, c.Expiry, c.SecurityCode, c.BillingCountryCode, c.BillingPostalCode);
                    var view = await svc.SaveCardAsync(buyerId, card, ct);
                    return Results.Created($"api/payment-methods/{view.PaymentMethodId}", view);
                }))
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
