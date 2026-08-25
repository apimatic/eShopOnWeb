using System.Security.Claims;
using System.Threading;
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

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IRepository<SavedPaymentMethod> repo,
                   IPayPalService paypal, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var userId = user.Identity?.Name ?? "";
                return await HandleAsync(paymentMethodId, repo, paypal, userId, ct);
            })
            .Produces(204)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, IRepository<SavedPaymentMethod> repo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(int paymentMethodId, IRepository<SavedPaymentMethod> repo,
        IPayPalService paypal, string userId, CancellationToken ct)
    {
        var spec = new SavedPaymentMethodByIdAndUserSpec(paymentMethodId, userId);
        var method = await repo.FirstOrDefaultAsync(spec, ct);

        if (method is null) return Results.NotFound();

        try
        {
            await paypal.DeleteVaultedCardAsync(method.VaultToken, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Delete vaulted card failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }

        await repo.DeleteAsync(method, ct);
        return Results.NoContent();
    }
}
