using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove one of the caller's saved cards from the
/// vault and the local store. Afterwards it no longer appears in the list and can no longer be used
/// to pay. Shopper-scoped — one shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int paymentMethodId,
                ClaimsPrincipal user,
                IRepository<SavedPaymentMethod> paymentMethodRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var saved = await paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), ct);
                if (saved is null)
                {
                    return Results.NotFound(new { message = $"Saved card {paymentMethodId} was not found." });
                }

                await processor.DeleteVaultedCardAsync(saved.VaultToken, ct);
                await paymentMethodRepository.DeleteAsync(saved, ct);

                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
