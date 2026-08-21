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

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. The response describes
/// the card safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SaveCardCommand, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(new SaveCardCommand(PaymentUser.BuyerId(user), request, ct), service);
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SaveCardCommand command, IPaymentMethodService service)
    {
        if (string.IsNullOrWhiteSpace(command.Card.Number) || string.IsNullOrWhiteSpace(command.Card.Expiry))
        {
            return Results.BadRequest("A card number and expiry are required to save a card.");
        }

        var card = PaymentApiMapper.ToCardDetails(command.Card);
        var saved = await service.SaveCardAsync(command.BuyerId, card, command.Ct);

        var response = new SaveCardResponse
        {
            PaymentMethodId = saved.Id,
            CardBrand = saved.CardBrand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public record SaveCardCommand(string BuyerId, CardDto Card, CancellationToken Ct);
