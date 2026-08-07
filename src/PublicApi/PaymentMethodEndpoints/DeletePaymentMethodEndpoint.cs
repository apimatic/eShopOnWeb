using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. After this, the card no longer appears among
/// the caller's saved cards and can no longer be used to pay: the local record — the app's only handle
/// on the vault token — is removed, and the vault token itself is deleted at PayPal (best effort).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user,
                   IRepository<SavedPaymentMethod> paymentMethodRepository, IPaymentService paymentService,
                   IAppLogger<DeletePaymentMethodEndpoint> logger, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var card = await paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct);
                if (card is null)
                {
                    return Results.NotFound($"Saved card {paymentMethodId} was not found.");
                }

                // Delete the vault token at PayPal first; tolerate a provider hiccup so a stuck token can
                // never keep a shopper from removing their card. Removing the local record below is what
                // authoritatively makes the card unusable through this API.
                try
                {
                    await paymentService.DeleteVaultedCardAsync(card.VaultId, ct);
                }
                catch (PaymentException ex)
                {
                    logger.LogWarning("Best-effort PayPal vault deletion failed for saved card {0}: {1}", paymentMethodId, ex.Message);
                }

                await paymentMethodRepository.DeleteAsync(card, ct);

                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Removes a saved card", "Deletes the saved card and its PayPal vault token."));
    }
}
