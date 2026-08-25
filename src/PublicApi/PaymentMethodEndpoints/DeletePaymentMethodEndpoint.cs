using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IRepository<PaymentMethod> pmRepo, IPayPalService payPal, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var method = await pmRepo.FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId));
                if (method == null) return Results.NotFound();

                await payPal.DeleteVaultTokenAsync(method.PayPalTokenId);
                await pmRepo.DeleteAsync(method);

                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
