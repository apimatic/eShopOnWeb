using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardInput? Card { get; set; }
    public string? Label { get; set; }
}

/// <summary>A saved card, described safely enough to recognise — never full card details.</summary>
public record SavedCardView(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? Label);

/// <summary>Saves a card in PayPal's vault for the signed-in shopper; stores only a safe descriptor.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Save a card for reuse", Tags = new[] { "PaymentMethodEndpoints" })]
            async (SavePaymentMethodRequest request, ISavedCardService savedCardService, HttpContext http, CancellationToken ct) =>
            {
                if (request.Card is null)
                {
                    throw new OrderRequestInvalidException("Card details are required to save a payment method.");
                }
                var buyerId = http.User.GetBuyerId();
                var card = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Label, ct);

                var response = new SavedCardView(card.Id, card.Brand, card.Last4, card.Expiry, card.Label);
                return Results.Created($"api/payment-methods/{card.Id}", response);
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
