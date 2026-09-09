using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card to vault for the signed-in shopper.</summary>
public class SavePaymentMethodRequest : BaseRequest
{
    public CardModel Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardModel? PaymentMethod { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely for
/// recognition; full card details go only to PayPal's vault, never this application's store.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService savedCardService, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                var card = await savedCardService.SaveCardAsync(buyerId, request.Card.ToPayPalCardInput(), ct);

                return Results.Created($"api/payment-methods/{card.Id}",
                    new SavePaymentMethodResponse(request.CorrelationId())
                    {
                        PaymentMethodId = card.Id,
                        PaymentMethod = SavedCardModel.From(card)
                    });
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
