using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeleteCardEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{id:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int id,
                   IRepository<SavedCard> cardRepo,
                   IPayPalService payPal,
                   ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var card = await cardRepo.FirstOrDefaultAsync(new SavedCardByIdAndBuyerSpec(id, buyerId));
                if (card == null)
                    return Results.NotFound();

                try
                {
                    await payPal.DeleteVaultTokenAsync(card.VaultToken);
                }
                catch (PayPalException ex)
                {
                    // If PayPal says not found, the token was already gone — treat as success
                    if (ex.HttpStatus != 404)
                        return Results.BadRequest(new { error = $"Could not delete vault token: {ex.Message}" });
                }

                await cardRepo.DeleteAsync(card);

                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
