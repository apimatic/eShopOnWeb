using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext ctx,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalClient paypal) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId);
                var pm = await pmRepo.GetBySpecAsync(spec);
                if (pm == null)
                    return Results.NotFound(new { error = "Payment method not found." });

                try
                {
                    await paypal.DeleteVaultTokenAsync(pm.PayPalVaultId);
                }
                catch (PayPalException ex) when (ex.HttpStatus == 404)
                {
                    // Already deleted from PayPal vault — continue to remove from DB
                }
                catch (PayPalException ex)
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = $"Failed to delete vault token: {ex.Message}",
                        paypalCode = ex.PayPalName
                    });
                }

                await pmRepo.DeleteAsync(pm);
                return Results.NoContent();
            })
            .Produces(204)
            .ProducesProblem(404)
            .WithTags("PaymentMethodEndpoints");
    }
}
