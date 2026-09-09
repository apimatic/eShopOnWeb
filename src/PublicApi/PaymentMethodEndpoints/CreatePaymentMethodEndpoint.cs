using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely; full card details are never stored or returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToPayPalCard(), request.Alias);
        var dto = SavedPaymentMethodDto.From(saved);
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            CardBrand = dto.CardBrand,
            CardLast4 = dto.CardLast4,
            CardholderName = dto.CardholderName,
            Expiry = dto.Expiry,
            Alias = dto.Alias,
            CreatedAt = dto.CreatedAt
        };
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", response);
    }
}
