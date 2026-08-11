using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — saves (vaults) a card for the signed-in shopper. The response
/// describes the card safely; full card details are never stored or returned.
/// </summary>
public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, ISavedCardService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ISavedCardService service, HttpContext ctx) =>
                await HandleAsync(request, service, ctx))
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, ISavedCardService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);

        if (request?.Card is null)
        {
            throw new PaymentValidationException("Card details are required to save a payment method.");
        }

        var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Alias, ctx.RequestAborted);

        return Results.Created($"api/payment-methods/{saved.Id}",
            new SaveCardResponse(saved.Id, saved.Describe(), saved.CardBrand, saved.CardLast4, saved.CardExpiry, saved.Alias));
    }
}

public class SaveCardRequest
{
    public CardRequest? Card { get; set; }
    public string? Alias { get; set; }
}

/// <summary>Response carrying the saved card's identifier as a top-level field.</summary>
public record SaveCardResponse(int PaymentMethodId, string Description, string? Brand, string? Last4, string? Expiry, string? Alias);
