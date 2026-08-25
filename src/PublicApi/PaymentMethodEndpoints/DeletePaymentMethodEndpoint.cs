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
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Removes a saved payment method belonging to the caller.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IRepository<PaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId,
                IRepository<PaymentMethod> pmRepo,
                IPayPalPaymentService payPalService,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = GetBuyerId(user);
                var pm = await pmRepo.GetBySpecAsync(new PaymentMethodByIdSpec(paymentMethodId), ct);

                if (pm == null || pm.BuyerId != buyerId)
                    return Results.NotFound(new { error = "Payment method not found." });

                try
                {
                    await payPalService.DeleteVaultedCardAsync(pm.PayPalVaultId, ct);
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                await pmRepo.DeleteAsync(pm, ct);
                return Results.NoContent();
            })
            .Produces(204)
            .Produces(404)
            .Produces(422)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IRepository<PaymentMethod> repo)
        => throw new System.NotImplementedException();

    private static string GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name)
        ?? user.FindFirstValue("sub")
        ?? user.Identity?.Name
        ?? throw new System.UnauthorizedAccessException();
}
