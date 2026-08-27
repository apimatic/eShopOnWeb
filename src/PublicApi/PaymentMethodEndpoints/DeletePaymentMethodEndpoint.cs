using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, both from the PayPal vault and
/// from this application. Afterwards it can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user,
                IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway) =>
            {
                return await HandleAsync(paymentMethodId, user, savedCardRepository, gateway);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user,
        IRepository<SavedCard> savedCardRepository, IPayPalGateway gateway)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await savedCardRepository.GetByIdAsync(paymentMethodId);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            // Same response whether the card does not exist or belongs to another shopper.
            throw new SavedCardNotFoundException(paymentMethodId);
        }

        await gateway.DeleteVaultedCardAsync(savedCard.VaultTokenId);
        await savedCardRepository.DeleteAsync(savedCard);

        return Results.NoContent();
    }
}
