using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext ctx,
                   IRepository<SavedPaymentMethod> methodRepo,
                   IPayPalService paypal) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

                var method = await methodRepo.GetByIdAsync(paymentMethodId);
                if (method == null) return Results.NotFound();
                if (method.BuyerIdentityGuid != username)
                    return Results.Problem("Access denied.", statusCode: 403);

                await paypal.DeleteVaultedCardAsync(method.PayPalVaultId);
                await methodRepo.DeleteAsync(method);

                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<SavedPaymentMethod> dependency)
        => throw new NotImplementedException();
}
